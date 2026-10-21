namespace AI.Application.Tools;

using Chat;
using Chats;
using Projects;
using Settings;
using Contracts.Chat;
using Contracts.Chats;
using Contracts.Tools;
using Contracts.Runs;
using Contracts.Settings;
using Contracts.Workspace;
using Workspace;
using Instructions;
using Contracts.Instructions;
using Contracts.Skills;
using Skills;
using Usage;
using Plans;
using Contracts.Plans;
using Contracts.Usage;
using Commands;
using System.Text;
using System.Text.Json;
using ChatKind = AI.Domain.Chats.ChatKind;

/// <remarks>
/// The tool session factory arrives as a factory rather than an instance. A session is opened per
/// run, never at construction, and the graph is circular — a tool can start a nested run, so the
/// tools depend on this agent and this agent depends on the tools. Asking for the instance here
/// closed that loop at construction time and yielded an agent holding a null factory, which failed
/// only when a run started rather than when the container was built.
/// </remarks>
public sealed class ChatAgent(IChatCompletionClient completion, Func<IToolSessionFactory> sessions,
    IProjectService projects, IGlobalSettingsRepository settings, IToolPolicyResolver policies,
    IWorkspaceChangeTracker workspace, IToolResultModelProjector modelProjector,
    IToolResultCodec toolResultCodec, IChatContextPlanner contextPlanner,
    IContextPlanDiagnostics contextDiagnostics, IChatTransportActivity transport,
    IAdaptiveContextPolicy contextPolicy, IToolCatalogRegistry toolCatalog, IToolDiscoveryGuidance toolGuidance,
    IModelContentCheckpointService checkpoints, IModelInstructionRegistry instructions,
    IModelInstructionComposer instructionComposer, IModelInstructionDiagnostics instructionDiagnostics,
    IStandingInstructions standingInstructions, IContextTokenEstimator tokenEstimator, ISkillGuide skillGuide,
    ISkillRouting skillRouting, ITokenUsageMeter usageMeter, IHistoryCheckpointService historyCheckpoints,
    IClock clock, IIdGenerator ids, Func<IChatKindPolicyRegistry> kindPolicies, IChatPlans plans,
    IConnectionContextWindowDetector contextWindows, IExecutableLocator executables,
    Func<IToolAutoApprover> autoApprover) : IChatAgent
{
    public async Task<WorkspaceChangeSet> RunAsync(Guid projectId, Guid chatId, Guid branchId, ChatCompletionRequest request,
        Func<ChatCompletionMessage, CancellationToken, Task> persist,
        Func<string, CancellationToken, Task> text,
        Func<ToolActivity?, CancellationToken, Task> activity,
        Func<ChatTransportWait?, CancellationToken, Task> transportActivity,
        Func<AgentTool, string, long, ToolCallPosition, CancellationToken, Task<ToolApprovalAction>> approve,
        CancellationToken cancellationToken,
        bool interactive = true,
        Guid? parentBranchId = null,
        Func<string?, CancellationToken, Task>? draft = null,
        Func<ContextUsage, CancellationToken, Task>? contextUsage = null,
        Func<string, CancellationToken, Task>? draftToolCall = null,
        bool overlayPromptsAllowed = true,
        Func<CancellationToken, Task<IReadOnlyList<ChatCompletionMessage>>>? asides = null,
        Func<string, CancellationToken, Task>? stalledReport = null,
        Func<CancellationToken>? joinSignal = null,
        int? maxModelSteps = null)
    {
        var estimator = tokenEstimator.ForModel(request.Model);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var transportScope = transport.BeginScope(transportActivity);
        var deadline = new TurnDeadline(source, TimeSpan.FromHours(24));
        var token = source.Token;
        Task Draft(string? content) => draft?.Invoke(content, token) ?? Task.CompletedTask;
        ContextPlan? lastPlan = null;
        Task Usage(ContextPlan plan, long answerTokens = 0) =>
            contextUsage?.Invoke(ToUsage(plan, answerTokens), token) ?? Task.CompletedTask;
        async Task Answer(string answer)
        {
            if (lastPlan is not null)
                await Usage(lastPlan, estimator.EstimateMessages([new ChatCompletionMessage("assistant", answer)]));
            await text(answer, token);
        }
        var global = await settings.LoadAsync(token);
        var configuredConnection = request.CredentialProfileId is { } connectionId
            ? global.Connections.SingleOrDefault(item => item.Id == connectionId)
            : global.Connections.SingleOrDefault(item => item.Model == request.Model
                && item.BaseUrl.TrimEnd('/') == request.BaseUrl.TrimEnd('/'));
        if (configuredConnection is not null)
        {
            configuredConnection = configuredConnection with { Model = request.Model };
            // Without a window set by hand, the catalog's figure for this model beats the fallback.
            configuredConnection = await contextWindows.DetectAsync(configuredConnection, token);
        }
        var project = await projects.GetAsync(projectId, token) ?? throw new InvalidOperationException("Project not found.");
        // Every server is gated the same way: enabled and not denied globally, and not switched off
        // for this project. A server that fails the test is never started, so nothing it could
        // offer reaches the model or costs a process.
        bool Enabled(Guid serverId) =>
            global.McpServers.SingleOrDefault(server => server.Id == serverId) is { Enabled: true, Policy: not "Deny" }
            && project.McpServers.SingleOrDefault(server => server.Id == serverId) is not { Enabled: false };
        var kindPolicy = kindPolicies().Resolve(request.Kind == default ? ChatKind.Conversation : request.Kind);
        kindPolicy.ValidateState(request.KindState, request.KindStateVersion);
        var behavior = kindPolicy.Behavior;
        var servers = global.McpServers.Select(server => server.Id).Where(Enabled)
            .Where(kindPolicy.AllowsServer).ToHashSet();
        var grants = project.DirectoryGrants.Where(_ => behavior.AllowDirectoryGrants)
            .Select(grant => new ToolDirectoryGrant(grant.CanonicalRoot, grant.Recursive, grant.ToolNames)).ToArray();
        var subtask = parentBranchId is not null;
        var run = new ToolRunContext(projectId, chatId, branchId, interactive, kindPolicy.Kind, request.KindState,
            request.KindStateVersion, overlayPromptsAllowed, subtask);
        using var catalogScope = toolCatalog.Begin(run);
        using var instructionScope = instructions.Begin(run);
        var plansAllowed = !subtask && servers.Contains(AppMcpServer.Id) && kindPolicy.AllowsTool(AppMcpServer.Id, ChatPlanResults.ToolName);
        using var planScope = plansAllowed ? await plans.BeginAsync(run, token) : null;
        // Waiting for the person stops the plan's working clock as it stops the turn's deadline.
        if (plansAllowed) deadline.PersonWait = () => plans.WaitForPerson(run);
        instructions.Upsert(run, new ModelInstruction("run.finishing", behavior.FinishingInstruction ?? FinishingInstruction,
            1_000, ModelInstructionLifetime.Run, Required: true,
            CompactContent: behavior.CompactFinishingInstruction ?? (behavior.FinishingInstruction is null ? CompactFinishingInstruction : null)));
        // Several app tools take the project and chat they act on as ids, and nothing else in the
        // context says which ones this run belongs to; a model left to guess reads lists to find them.
        if (servers.Contains(AppMcpServer.Id))
            instructions.Upsert(run, new ModelInstruction("run.context",
                $"This run: projectId {projectId}, chatId {chatId}, branchId {branchId}. The main branch id equals the chat id.",
                900, ModelInstructionLifetime.Run, Required: true));
        // A lead that has to rebuild its team's state reads every branch to do it; handed the state,
        // it reads none.
        if (request.TeamStatus is { Length: > 0 } teamStatus)
            instructions.Upsert(run, new ModelInstruction("run.team-status", teamStatus, 880,
                ModelInstructionLifetime.Run, Required: true));
        if (behavior.IncludeStandingInstructions)
            await UpsertStandingAsync(run, servers.Contains(AppMcpServer.Id) && !subtask, configuredConnection, token);
        if (subtask)
            instructions.Upsert(run, new ModelInstruction("run.subtask",
                "You are a focused background worker. Execute the assigned task directly with the tools already offered. "
                + "Do not plan, search for additional tools, inspect unrelated application state, or repeat successful reads and checks. "
                + "Keep spawn_subtask available, but delegate again only when the assigned work is itself independently divisible. "
                + "Keep necessary verification proportional and return a short factual result to the parent.",
                995, ModelInstructionLifetime.Run, Required: true));
        // Set while the application compacts the history itself, so its summary is accounted as a
        // compaction rather than as a checkpoint the model asked for.
        var compactingAhead = false;
        using var checkpointScope = checkpoints.Begin(run, request.Model,
            contextPolicy.ResolveCompaction(configuredConnection).HistoryKeepTokens, async (prompt, ct) =>
        {
            using var usageScope = usageMeter.Begin(new TokenUsageScope(
                compactingAhead ? TokenUsagePurpose.Compaction : TokenUsagePurpose.Checkpoint));
            return (await completion.CompleteAsync(request with
            {
                Message = prompt,
                ContextMessages = [new ChatCompletionMessage("user", prompt)],
                Tools = []
            }, ct)).Content;
        }, configuredConnection);
        var sessionFactory = servers.Count > 0 ? sessions() : null;
        var initialSession = sessionFactory is not null
            ? await sessionFactory.OpenAsync(grants, servers, run, token)
            : null;
        await using var session = initialSession is not null
            ? new RefreshableToolSession(sessionFactory!, initialSession, grants, servers, run)
            : null;
        var executionTools = session?.Tools.Where(IsExecutionTool).ToArray() ?? [];
        var approvalMode = executionTools.Length > 0
            ? await autoApprover().ModeAsync(projectId, chatId, branchId, token)
            : ToolApprovalMode.Ask;
        var installedPrograms = executionTools.Any(tool => tool.OriginalName == ToolPolicyResolver.ProcessRun)
            ? await InstalledProgramsAsync(executables, token)
            : [];
        var runKey = new WorkspaceRunKey(projectId, chatId, branchId);
        await workspace.BeginRunAsync(runKey, grants,
            parentBranchId is { } parent ? new WorkspaceRunKey(projectId, chatId, parent) : null, token);
        async Task<WorkspaceChangeSet> FinishAsync(string answer)
        {
            await Draft(null);
            await Answer(answer);
            var changes = await workspace.SnapshotAsync(runKey, token);
            if (parentBranchId is null && !changes.IsEmpty)
                changes = changes with { UndoId = await workspace.CaptureUndoAsync(runKey, changes, token) };
            await workspace.CompleteRunAsync(runKey, CancellationToken.None);
            return changes;
        }
        var context = request.ContextMessages?.ToList() ?? [new ChatCompletionMessage("user", request.Message)];
        var runStart = context.FindLastIndex(message => message.StartsTurn);
        var counts = context.Skip(Math.Max(0, runStart)).SelectMany(message => message.ToolCalls ?? [])
            .GroupBy(call => call.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var seenIds = context.SelectMany(message => message.ToolCalls ?? []).Select(call => call.Id).ToHashSet(StringComparer.Ordinal);

        // A model message carrying tool calls is only valid once every one of them has an answer.
        // When a batch is abandoned part-way — cancelled, or broken by a transport failure — the
        // call that failed and every call after it still have to be answered, or the stored history
        // becomes one the endpoint will reject and the run can never be resumed from.
        async Task AbandonAsync(IReadOnlyList<ChatToolCall> batch, int from, string reason)
        {
            for (var remaining = from; remaining < batch.Count; remaining++)
                await persist(ToolMessage(batch[remaining].Id,
                    Error(remaining == from ? reason : "Not executed: an earlier call in the same batch did not finish.")),
                    CancellationToken.None);
        }

        var empty = 0;
        var compactedAfterEmpty = false;
        var interrupted = 0;
        var truncated = 0;
        var continuedAnswer = new StringBuilder();
        var stalledSteps = 0;
        var modelSteps = 0;
        var criteriaAsked = false;
        var observedResults = new HashSet<(string Name, string Arguments, string Result)>();
        // The first call of each read this turn, by what it returned: a read repeated with the same
        // result is sent as a pointer to the first, not as a second copy of the same file.
        var firstReads = new Dictionary<(string Name, string Arguments, string Result), string>();
        var routed = false;
        // What the request carried in the previous step: kept, so the next request starts the same.
        var compactionMemory = new ContextCompactionMemory();
        IReadOnlyList<AgentTool>? sentTools = null;
        string? executionCapabilities = null;
        // After a failed or fruitless attempt the next waits until the request has grown again:
        // retried at every step, each would cost a summary.
        long compactAheadFrom = 0;
        // Runs mcp_app__run_skill for the routed playbook as if the model had called it: the call and its
        // result are persisted like any other, so the transcript shows the skill and a resumed run
        // sees its instructions. True when the playbook's instructions are now in the context.
        async Task<bool> LoadRoutedSkillAsync(AgentTool runSkill, SkillDefinition skill)
        {
            var call = new ChatToolCall($"route_{Guid.NewGuid():N}", runSkill.ModelDefinition.Name,
                JsonSerializer.Serialize(new { skillId = skill.Id, parameters = new { } }));
            ToolCallResult result;
            try
            {
                var arguments = session!.ValidateArguments(runSkill, call.Arguments);
                await Draft(null);
                var assistant = new ChatCompletionMessage("assistant", string.Empty, [call]);
                await persist(assistant, token);
                context.Add(assistant);
                seenIds.Add(call.Id);
                counts[call.Name] = counts.GetValueOrDefault(call.Name) + 1;
                await activity(new ToolActivity(call.Id, call.Name, arguments), token);
                try
                {
                    result = await session.CallAsync(runSkill, arguments, null, token);
                }
                catch (OperationCanceledException)
                {
                    await AbandonAsync([call], 0, "Invocation interrupted. Load the skill again if it is still needed.");
                    throw;
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    result = Error("The skill could not be loaded: " + error.Message);
                }
            }
            catch (ArgumentException)
            {
                // The schema refused the arguments before anything was persisted: leave it to the model.
                return false;
            }
            var message = ToolMessage(call.Id, result);
            await persist(message, token);
            context.Add(message);
            await activity(null, token);
            return !result.IsError;
        }

        // Told to stop calling tools, the model called one anyway. It still owes the reader an
        // account of the turn, so it is asked once more with no tools to call; the canned text is
        // only for an endpoint that cannot give even that.
        async Task<string> StalledReportAsync(IReadOnlyList<ChatCompletionMessage> messages)
        {
            await Draft(null);
            var report = new StringBuilder();
            try
            {
                await foreach (var chunk in completion.StreamAsync(request with { ContextMessages = messages, Tools = [] }, token))
                {
                    if (chunk.Content.Length == 0) continue;
                    report.Append(chunk.Content);
                    await Draft(chunk.Content);
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return StalledAnswer;
            }
            return string.IsNullOrWhiteSpace(report.ToString()) ? StalledAnswer : report.ToString();
        }

        while (true)
        {
            if (session is not null)
            {
                var latestProject = await projects.GetAsync(projectId, token)
                                    ?? throw new InvalidOperationException("Project not found.");
                var latestGrants = latestProject.DirectoryGrants
                    .Select(grant => new ToolDirectoryGrant(grant.CanonicalRoot, grant.Recursive, grant.ToolNames)).ToArray();
                if (await session.RefreshAsync(latestGrants, token))
                    await workspace.UpdateGrantsAsync(runKey, latestGrants, token);
            }

            // A continuation after truncation is the same answer carrying on, so its draft keeps
            // growing instead of starting over.
            if (truncated == 0) await Draft(null);
            checkpoints.Update(run, context);
            var modelContext = checkpoints.Apply(run, context);
            if (maxModelSteps is { } stepLimit && modelSteps >= stepLimit)
            {
                var report = $"I stopped after the {stepLimit}-step model-step budget. Review the tool transcript for completed work; the requested task may be unfinished.";
                if (stalledReport is not null) await stalledReport(report, token);
                return await FinishAsync(report);
            }
            var permitted = new List<AgentTool>();
            if (session is not null)
            {
                var candidates = session.Tools.Where(tool => kindPolicy.AllowsTool(tool.ServerId, tool.OriginalName))
                    .Where(tool => !subtask || tool.ServerId != AppMcpServer.Id || tool.OriginalName == "spawn_subtask").ToArray();
                var resolved = await policies.ResolveAsync(projectId, chatId, branchId, candidates, token);
                for (var index = 0; index < candidates.Length; index++)
                    if (resolved[index].Decision != "Deny") permitted.Add(candidates[index]);
                var capabilityText = ExecutionCapabilities(candidates, resolved, approvalMode, installedPrograms);
                if (capabilityText != executionCapabilities)
                {
                    executionCapabilities = capabilityText;
                    if (capabilityText is null) instructions.Remove(run, "run.execution-capabilities");
                    else instructions.Upsert(run, new ModelInstruction("run.execution-capabilities", capabilityText,
                        985, ModelInstructionLifetime.Run, Required: true));
                }
            }
            if (maxModelSteps is { } warningLimit && modelSteps == Math.Max(1, warningLimit - 5))
                instructions.Upsert(run, new ModelInstruction("run.subtask-step-warning",
                    "Only five model steps remain. Stop exploring and complete the requested output now; verify only the essential result.",
                    995, ModelInstructionLifetime.UntilAcknowledged, Required: true));
            if (behavior.PinnedTools is { Count: > 0 } pinnedTools) toolCatalog.Pin(run, pinnedTools);
            // Execution and basic file tools are core in the selector, so they are offered without
            // a pin: a pin every step would narrow the rest of the selection to a handful of tools.
            toolCatalog.Update(run, permitted);
            // Once per turn, before its first step: which skill fits the new message, and which tools
            // the first steps need. The tools are pinned before the selector cuts the list.
            if (!routed)
            {
                routed = true;
                if (!subtask && interactive && behavior.RouteSkills && servers.Contains(AppMcpServer.Id)
                    && await RouteAsync(run, context, permitted, token) is { } route)
                {
                    var runSkill = permitted.FirstOrDefault(tool =>
                        tool.ServerId == AppMcpServer.Id && tool.OriginalName == RunSkillTool);
                    var loaded = LoadsItself(route) && runSkill is not null
                                 && await LoadRoutedSkillAsync(runSkill, route.Skills[0]);
                    UpsertRouteHint(run, route, loaded);
                    // The loaded playbook is now in the context, which this step was planned without.
                    if (loaded) continue;
                }
            }
            // The tools stay offered: taking them away would change the request's cached start, and
            // a call made anyway ends the run below.
            var stalled = stalledSteps >= MaxStalledSteps;
            if (stalled)
                instructions.Upsert(run, new ModelInstruction("run.stalled",
                    "Several tool calls produced no new information. Do not call another tool. Answer now: what you "
                    + "found and did, and what is missing or blocking the rest.",
                    970, ModelInstructionLifetime.Request));
            if (!subtask && behavior.UseActiveSkills && servers.Contains(AppMcpServer.Id))
                await UpsertActiveSkillAsync(run, context, token);
            if (plansAllowed) UpsertPlan(run);
            // Every step is finished and only the criteria are left: the answer this step may write
            // would be dropped at the gate below, so the check is asked for now, before it is written.
            if (plansAllowed && !criteriaAsked && plans.Current(run) is { Steps.Count: > 0 } finished
                && finished.Steps.All(step => step.IsFinished) && OpenCriteria(run) is { Length: > 0 } unsettled)
            {
                criteriaAsked = true;
                instructions.Upsert(run, new ModelInstruction(CriteriaKey, CriteriaGate(unsettled), 960,
                    ModelInstructionLifetime.UntilAcknowledged));
            }
            var composition = instructionComposer.Compose(run, modelContext, configuredConnection);
            var pendingTools = toolCatalog.ConsumePinned(run);
            var selection = contextPolicy.Choose(configuredConnection, request.Message, composition.Messages, permitted,
                pendingTools, sentTools, composition.Trailing is null ? 0
                    : estimator.EstimateMessages([new ChatCompletionMessage("user", composition.Trailing)]));
            var selectedTools = selection.Tools;
            var available = selectedTools.Select(item => item.ModelDefinition).ToArray();
            if (toolGuidance.ForSelection(selection) is { } discovery)
                instructions.Upsert(run, new ModelInstruction("run.tool-discovery",
                    discovery,
                    990, ModelInstructionLifetime.Request));
            else instructions.Remove(run, "run.tool-discovery");
            var calls = new List<ChatToolCall>();
            var content = new StringBuilder();
            string? finish = null;
            var chunkCount = 0;
            composition = instructionComposer.Compose(run, modelContext, configuredConnection);
            // Measured with the instructions and tools the request carries: they share the window.
            var stepBudget = contextPolicy.ResolveCompaction(configuredConnection, estimator.EstimateTools(available),
                composition.Trailing is null ? 0 : estimator.EstimateMessages([new ChatCompletionMessage("user", composition.Trailing)]));
            checkpoints.UpdateBudget(run, stepBudget);
            var size = estimator.EstimateMessages(composition.Messages);
            if (contextPolicy.ShouldCompactAhead(stepBudget, size, compactAheadFrom))
            {
                bool compacted;
                compactingAhead = true;
                try
                {
                    compacted = await CompactAheadAsync(run, stepBudget, token);
                }
                finally
                {
                    compactingAhead = false;
                }
                if (compacted)
                {
                    // What was read before is now a summary, so reading it again is not a repeat.
                    observedResults.Clear();
                    firstReads.Clear();
                    modelContext = checkpoints.Apply(run, context);
                    composition = instructionComposer.Compose(run, modelContext, configuredConnection);
                }
                // Even an accepted checkpoint waits for more input before another automatic attempt.
                compactAheadFrom = estimator.EstimateMessages(composition.Messages) + stepBudget.RetryGrowthTokens;
            }
            // The guidance and any new checkpoint are now known. Select against what will really
            // be sent, releasing definitions whose protected protocol groups were summarized away.
            selection = contextPolicy.Choose(configuredConnection, request.Message, composition.Messages, permitted,
                pendingTools, sentTools, composition.Trailing is null ? 0
                    : estimator.EstimateMessages([new ChatCompletionMessage("user", composition.Trailing)]));
            selectedTools = selection.Tools;
            sentTools = selectedTools;
            available = selectedTools.Select(item => item.ModelDefinition).ToArray();
            // Check discovery against the final schemas after checkpoints and guidance changed
            // the budget. Never direct the model to a control tool that was itself omitted.
            if (toolGuidance.ForSelection(selection) is { } finalDiscovery)
                instructions.Upsert(run, new ModelInstruction("run.tool-discovery", finalDiscovery,
                    990, ModelInstructionLifetime.Request));
            else instructions.Remove(run, "run.tool-discovery");
            composition = instructionComposer.Compose(run, modelContext, configuredConnection);
            stepBudget = contextPolicy.ResolveCompaction(configuredConnection, estimator.EstimateTools(available),
                composition.Trailing is null ? 0 : estimator.EstimateMessages([new ChatCompletionMessage("user", composition.Trailing)]));
            checkpoints.UpdateBudget(run, stepBudget);
            contextDiagnostics.RecordToolSelection(request.Model, selection);
            instructionDiagnostics.RecordInstructions(request.Model, composition.Keys, composition.EstimatedTokens);
            var plan = await contextPlanner.PlanAsync(configuredConnection, request.Model, composition.Messages, available,
                new CompletionClientSummarizer(completion, request, usageMeter), stepBudget.SummaryTargetTokens, token,
                composition.Trailing, compactionMemory);
            contextDiagnostics.Record(request.Model, plan, composition.Messages.Count, available.Length);
            lastPlan = plan;
            if (plan.HistorySummary is { } written)
            {
                // The model has just summarized the older turns to make this request fit. Kept, that
                // summary stands in for them in the rest of this run and in later turns, instead of
                // being written again for every request that would not fit without it.
                var kept = new HistoryCheckpoint(ids.Create(), written.UpToMessageId, written.Text, written.CoveredMessages,
                    written.SourceCharacters, request.Model, clock.UtcNow, HistoryCheckpointOrigin.Automatic);
                try
                {
                    await historyCheckpoints.AddAsync(projectId, chatId, kept, token);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // Unkept, the summary still serves this run; later turns write their own.
                }
                checkpoints.PinHistory(run, kept);
                observedResults.Clear();
                firstReads.Clear();
            }
            await Usage(plan);
            if (!plan.Fits) throw new ContextWindowExceededException(plan);
            try
            {
                modelSteps++;
                await foreach (var chunk in completion.StreamAsync(
                                   request with { ContextMessages = plan.Messages, Tools = available }, token))
                {
                    chunkCount++;
                    if (chunk.ToolCalls is { } received) calls.AddRange(received);
                    if (chunk.FinishReason is { Length: > 0 } reason) finish = reason;
                    if (chunk is { ToolCallsStarted: true, ToolCallName: { Length: > 0 } starting } && draftToolCall is not null)
                        await draftToolCall(starting, token);
                    if (chunk.Content.Length == 0) continue;
                    content.Append(chunk.Content);
                    await Draft(chunk.Content);
                }
                interrupted = 0;
            }
            // A stream that broke off part-way has left nothing behind: the step is persisted only
            // once it is complete, so none of its calls ran. Asking for the step again is what the
            // person would do with Retry, and a team branch has nobody watching to press it.
            catch (Exception error) when (StreamBroke(error, token) && ++interrupted <= MaxInterruptedSteps)
            {
                await Draft(null);
                await Task.Delay(TimeSpan.FromSeconds(2 * interrupted), token);
                continue;
            }
            if (calls.Count == 0 && content.Length == 0)
            {
                // An endpoint that answers with nothing at all has not decided to stop — it has
                // failed to answer, and usually only this once. Nothing is persisted for an empty
                // turn. Keep every unacknowledged instruction: an empty provider response did not
                // act on it.
                var attempt = ++empty;
                instructionDiagnostics.RecordEmptyResponse(request.Model, attempt, finish, chunkCount, counts.Count > 0);
                instructions.Upsert(run, new ModelInstruction("response.empty",
                    "Your last response was empty. Give your answer, or call a tool if the request still needs work.",
                    980, ModelInstructionLifetime.UntilAcknowledged));
                // Long contexts are where weaker models stop answering, well inside the window they
                // declare. Before giving up, a turn with something worth summarizing is compacted once
                // and asked again; a short one has nothing to gain and fails as before.
                if (attempt > MaxEmptyTurns && !compactedAfterEmpty && await CompactAheadAsync(run, stepBudget, token))
                {
                    compactedAfterEmpty = true;
                    empty = 0;
                    observedResults.Clear();
                    firstReads.Clear();
                    modelContext = checkpoints.Apply(run, context);
                    compactAheadFrom = estimator.EstimateMessages(instructionComposer.Compose(run, modelContext, configuredConnection).Messages)
                        + stepBudget.RetryGrowthTokens;
                    continue;
                }
                if (attempt > MaxEmptyTurns)
                    throw new InvalidOperationException(
                        $"The model returned an empty response {attempt} times "
                        + $"(finish reason: {finish ?? "none"}, chunks: {chunkCount}).");
                await Task.Delay(TimeSpan.FromSeconds(empty), token);
                continue;
            }

            instructionComposer.Acknowledge(run, composition);
            empty = 0;
            if (calls.Count == 0 && Truncated(finish))
            {
                // The answer was cut at the token ceiling, not finished. Returning here would end
                // the run as a success and leave a sentence hanging, which is indistinguishable to
                // the reader from the model choosing to stop there. Instead the partial answer goes
                // back as context and the model is asked to carry on: nothing is persisted for this
                // turn, and the caller keeps appending to the same streamed message, so the
                // continuation arrives as one uninterrupted answer.
                if (++truncated > MaxTruncatedTurns)
                    throw new InvalidOperationException(
                        $"The model's answer was cut off at the token limit {MaxTruncatedTurns} times in a row.");
                context.Add(new ChatCompletionMessage("assistant", content.ToString()));
                continuedAnswer.Append(content);
                instructions.Upsert(run, new ModelInstruction("response.continue-after-truncation",
                    ContinueAfterTruncation, 900, ModelInstructionLifetime.UntilAcknowledged));
                continue;
            }

            truncated = 0;
            // A response without tool calls ends the turn, as the models are trained to: it is the
            // answer, published as written.
            if (calls.Count == 0)
            {
                // The plan's criteria are settled before the answer, once: an answer written over
                // criteria nobody checked reads as done when it may not be. The draft is dropped and
                // the step asked again; a second answer stands, whatever the criteria say.
                if (plansAllowed && !criteriaAsked && OpenCriteria(run) is { Length: > 0 } open)
                {
                    criteriaAsked = true;
                    continuedAnswer.Clear();
                    await Draft(null);
                    instructions.Upsert(run, new ModelInstruction(CriteriaKey, CriteriaGate(open), 960,
                        ModelInstructionLifetime.UntilAcknowledged));
                    continue;
                }
                var finalText = continuedAnswer.ToString() + content;
                continuedAnswer.Clear();
                return await FinishAsync(finalText);
            }

            // Told to stop calling tools, the model called one anyway: the loop ends here rather than
            // going round again.
            if (stalled)
            {
                var report = await StalledReportAsync(plan.Messages);
                if (stalledReport is not null) await stalledReport(report, token);
                return await FinishAsync(report);
            }

            continuedAnswer.Clear();
            var preamble = content.ToString();
            if (calls.Any(call => !seenIds.Add(call.Id)))
                throw new InvalidOperationException("Duplicate tool call IDs or excessive calls.");
            // A criterion of the plan may cite any of these as its evidence, the batch's own included.
            if (plansAllowed) foreach (var call in calls) plans.NoteCall(run, call.Id);
            var assistant = new ChatCompletionMessage("assistant", preamble, calls.ToArray());
            // Cleared first so the publication that carries the preamble also drops its draft:
            // the text changes owner without a frame showing it twice or not at all.
            await Draft(null);
            await persist(assistant, token); // Durable intent before any side effect.
            context.Add(assistant);
            checkpoints.Update(run, context);
            var toolImages = new List<string>();
            // A step is stalled only when none of its calls told the model anything new. An error
            // is information too, unless it is the same error for the same call again.
            var learned = false;
            // Adjacent read-only calls that need nobody's confirmation start together, so a batch
            // takes as long as its slowest call rather than all of them in turn. Mutating, waiting
            // or approval-bound calls are barriers. Results are still recorded in the batch's order.
            using var alongside = CancellationTokenSource.CreateLinkedTokenSource(token);
            var reads = new Dictionary<int, StartedRead>();
            try
            {
                for (var index = 0; index < calls.Count; index++)
                {
                    if (!reads.ContainsKey(index))
                        foreach (var read in await StartReadsAsync(calls, index, selectedTools, session, counts,
                                     projectId, chatId, branchId, runKey, alongside.Token, token))
                            reads.Add(read.Key, read.Value);
                    var call = calls[index];
                    ToolCallResult result;
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        if (reads.TryGetValue(index, out var read))
                        {
                            counts[call.Name] = counts.GetValueOrDefault(call.Name) + 1;
                            await activity(new ToolActivity(call.Id, call.Name, read.Arguments), token);
                            result = await read.Result;
                            await workspace.RecordEffectAsync(runKey, read.Tool.Descriptor, read.Arguments, token);
                        }
                        else
                        {
                            var tool = selectedTools.SingleOrDefault(item => item.ModelDefinition.Name == call.Name)
                                ?? throw new ArgumentException(toolGuidance.ForUnavailableCall(call.Name, selectedTools, permitted));
                            var arguments = session!.ValidateArguments(tool, call.Arguments);
                            var policy = await PolicyAsync(projectId, chatId, branchId, tool, arguments, token);
                            // What the rules were checked against is what runs: process_run's bare program
                            // name is pinned to the file PATH found for it, and the card shows that.
                            if (policy.Command?.Subject.Arguments is { } pinned) arguments = pinned;
                            if (tool.OriginalName is "process_run" or "cs_run" or "trigger_wait")
                            {
                                var input = System.Text.Json.Nodes.JsonNode.Parse(arguments)!;
                                var policyTimeoutMs = (int)policy.TimeoutSeconds * 1000;
                                input["timeoutMs"] = Math.Min(input["timeoutMs"]?.GetValue<int>() ?? policyTimeoutMs, policyTimeoutMs);
                                arguments = input.ToJsonString();
                            }
                            var count = counts.GetValueOrDefault(call.Name) + 1;
                            counts[call.Name] = count;
                            if (policy.Decision == "Deny") result = Error("Tool denied by current policy.");
                            else if (count > policy.MaxCalls) result = ToolCallLimitError(call.Name, count, policy);
                            // Asking to be allowed to ask is one prompt too many: the confirmation and the
                            // question put the same decision to the same person twice, and the first one
                            // tells them nothing the second does not. Deny still applies, above, so someone
                            // who does not want to be asked at all still has a way to say so.
                            else if (policy.Decision == "Ask" && !AsksTheUser(tool)
                                     && await deadline.WhileWaitingForAPersonAsync(() => approve(tool, arguments, policy.TimeoutSeconds,
                                         new ToolCallPosition(index + 1, calls.Count), token)) == ToolApprovalAction.Deny)
                                // Worded for both callers: a person declining at the card, and a background run that
                                // has nobody to ask and refuses anything needing confirmation.
                                result = Error("This invocation was not approved. Do not retry it.");
                            else
                            {
                                var activeCall = new ToolActivity(call.Id, call.Name, arguments);
                                await activity(activeCall, token);
                                var current = await PolicyAsync(projectId, chatId, branchId, tool, arguments, token);
                                if (current.Decision == "Deny") result = Error("Policy changed before execution. Submit a new invocation.");
                                else
                                {
                                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                                    // A wait gives way to a message joining the turn: the model waits for
                                    // something to happen, and the person writing to it is that something.
                                    var joined = tool.OriginalName == "trigger_wait"
                                        ? joinSignal?.Invoke() ?? CancellationToken.None
                                        : CancellationToken.None;
                                    using var wake = joined.Register(timeout.Cancel);
                                    // A tool that is waiting on a person is not a tool that has gone quiet.
                                    // Both clocks are off while it waits: the silence timer, which would
                                    // kill the question in under a minute, and the turn's own 24-hour limit, which
                                    // must not be spent on time the person took to read it.
                                    var asking = AsksTheUser(tool);
                                    // Leave a short transport margin for execution tools to report their own timeout.
                                    var patience = asking
                                        ? null
                                        : new Patience(timeout, TimeSpan.FromSeconds(policy.TimeoutSeconds + 2), MaxCallDuration);
                                    // Progress arrives on the transport's own thread while the call is in
                                    // flight, so it is forwarded fire-and-forget: a slow subscriber must
                                    // not be able to stall the tool it is reporting on.
                                    var progress = new Progress<ToolProgress>(update =>
                                    {
                                        patience?.Renew();
                                        _ = activity(
                                            activeCall with { Progress = update.Progress, Total = update.Total, Message = update.Message },
                                            CancellationToken.None);
                                    });
                                    // The baseline has to exist before the call, not after: once a write
                                    // lands there is nothing left to compare against.
                                    await workspace.RecordIntentAsync(runKey, tool.Descriptor, arguments, token);
                                    try
                                    {
                                        result = asking
                                            ? await deadline.WhileWaitingForAPersonAsync(
                                                () => session.CallAsync(tool, arguments, progress, timeout.Token))
                                            : await session.CallAsync(tool, arguments, progress, timeout.Token);
                                    }
                                    // A call that outlived its own policy timeout has failed; the turn has
                                    // not. Letting that reach the outer handler abandoned every call after
                                    // it and ended the run, so one slow server took the whole batch with
                                    // it. The run's own cancellation still does exactly that, which is the
                                    // difference this guard draws.
                                    catch (OperationCanceledException) when (!token.IsCancellationRequested && joined.IsCancellationRequested)
                                    {
                                        result = Text("The wait ended early because a new message joined this turn. "
                                            + "It follows these results; read it before deciding whether to wait again.");
                                    }
                                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                                    {
                                        result = Error($"The tool went silent for {policy.TimeoutSeconds} seconds. "
                                            + "Its effects may have occurred. Do not automatically repeat it.");
                                    }

                                    await workspace.RecordEffectAsync(runKey, tool.Descriptor, arguments, token);
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        await AbandonAsync(calls, index,
                            "Invocation interrupted or timed out. Its effects may have occurred. Do not automatically repeat it.");
                        throw;
                    }
                    catch (Exception error) when (error is ArgumentException or JsonException)
                    {
                        result = Error(error.Message);
                    }
                    catch (Exception)
                    {
                        // A transport failure can happen after a side effect. Record uncertainty and stop.
                        await AbandonAsync(calls, index, "Tool transport failed; outcome unknown. Do not automatically repeat it.");
                        throw;
                    }
                    var seen = (call.Name, call.Arguments, result.ModelContent);
                    var repeated = !result.IsError
                                   && selectedTools.FirstOrDefault(item => item.ModelDefinition.Name == call.Name) is { } used
                                   && RunsAlongside(used) && !firstReads.TryAdd(seen, call.Id);
                    var message = ToolMessage(call.Id, repeated ? result with { ModelContent = RepeatedRead(firstReads[seen]) } : result);
                    await persist(message, token);
                    context.Add(message);
                    toolImages.AddRange(result.Content.Where(item => item.Kind == ToolContentKind.Image)
                        .Select(item => item.AssetId).OfType<string>());
                    await activity(null, token);
                    learned |= observedResults.Add((call.Name, call.Arguments, result.ModelContent));
                }
            }
            finally
            {
                // A batch left early, by an error or a stop, leaves no read running behind it.
                await alongside.CancelAsync();
                foreach (var read in reads.Values)
                    try { await read.Result; }
                    catch (Exception) { /* Its outcome went with the batch. */ }
            }
            stalledSteps = learned ? 0 : stalledSteps + 1;
            if (toolImages.Count > 0 && configuredConnection?.ImageInput != ImageInputMode.Disabled)
                context.Add(new ChatCompletionMessage("user", "Images returned by the preceding tools:",
                    IsContextSummary: true, ImageAssetIds: toolImages));
            // Every call of the batch has its result, so a message added meanwhile can join here.
            // It is new information, so steps that found nothing new no longer count against the turn.
            if (asides is not null)
                foreach (var aside in await asides(token))
                {
                    context.Add(configuredConnection?.ImageInput == ImageInputMode.Disabled
                        ? aside with { ImageAssetIds = null }
                        : aside);
                    stalledSteps = 0;
                }
        }
    }

    private static bool IsExecutionTool(AgentTool tool) =>
        tool.OriginalName is ToolPolicyResolver.ProcessRun or "cs_run";

    private static async Task<string[]> InstalledProgramsAsync(
        IExecutableLocator executables, CancellationToken cancellationToken)
    {
        string[] names = ["pwsh", "powershell", "python", "python3", "bash", "dotnet", "node", "git", "cmd"];
        var found = await Task.WhenAll(names.Select(async name =>
            await executables.LocateAsync(name, null, cancellationToken) is null ? null : name));
        return found.OfType<string>().ToArray();
    }

    private static string? ExecutionCapabilities(
        IReadOnlyList<AgentTool> tools,
        IReadOnlyList<EffectiveToolPolicy> policies,
        ToolApprovalMode approvalMode,
        string[] installedPrograms)
    {
        var offered = tools.Select((tool, index) => (Tool: tool, Policy: policies[index]))
            .Where(item => IsExecutionTool(item.Tool)).ToArray();
        if (offered.Length == 0) return null;
        var text = new StringBuilder("Execution capabilities detected for this run:");
        foreach (var item in offered)
        {
            var state = item.Policy.Decision switch
            {
                "Allow" => "allowed by current policy",
                "Deny" => "denied by current policy",
                _ when approvalMode == ToolApprovalMode.FullAccess => "allowed by FullAccess mode",
                _ when approvalMode == ToolApprovalMode.Auto => "subject to automatic risk assessment",
                _ => "requires user approval unless a narrower saved rule allows the intended invocation"
            };
            text.Append("\n- ").Append(item.Tool.OriginalName).Append(": ").Append(state).Append('.');
        }
        if (offered.Any(item => item.Tool.OriginalName == ToolPolicyResolver.ProcessRun))
        {
            text.Append("\n- Programs currently found on PATH for process_run: ")
                .Append(installedPrograms.Length == 0 ? "none from the standard probe list" : string.Join(", ", installedPrograms))
                .Append(". Other programs may exist; do not assume one without checking.")
                .Append(" process_run permissions can be argument-specific, so a saved command rule may allow an intended call even when the general tool state is Ask.");
        }
        if (offered.Any(item => item.Tool.OriginalName == "cs_run" && item.Policy.Decision != "Deny"))
        {
            // One script in place of a chain of small calls is the cheapest step a run can take:
            // the model writes it once, and the calls it replaces would each cost a step and their
            // results in context.
            text.Append("\n- Prefer one cs_run script to a chain of small tool calls when the work is computation or ")
                .Append("bulk processing: counting, aggregating, parsing, comparing or transforming many files or records, ")
                .Append("exact arithmetic, or checking a hypothesis. Read inputs inside the script, pass parameters through ")
                .Append("`arguments` or `globals`, and return only the compact answer, as JSON for structured data, instead ")
                .Append("of printing raw contents. The first script of a run takes a second or two to start, later ones a few ")
                .Append("hundred milliseconds; each call is a fresh process, so keep a script self-contained. Use the code-run-csharp ")
                .Append("skill for long, risky or file-changing work.");
        }
        return text.ToString();
    }
    /// <summary>
    /// How many times a turn that produced nothing at all is asked again before the run gives up.
    /// Small on purpose: an endpoint that is genuinely answering nothing should be reported, not
    /// hammered, and the user is the one waiting through every attempt.
    /// </summary>
    private const int MaxEmptyTurns = 2;

    /// <summary>
    /// How many times in a row a step whose stream broke off is asked for again before the run
    /// fails. A provider that keeps dropping the same request is reported rather than retried forever.
    /// </summary>
    private const int MaxInterruptedSteps = 3;

    /// <summary>
    /// Whether a step failed because its stream broke off after it had started — fell silent, ended
    /// mid tool call, or lost its connection — rather than because the endpoint refused it.
    /// </summary>
    private static bool StreamBroke(Exception error, CancellationToken token) =>
        !token.IsCancellationRequested && error is ChatStreamInterruptedException or IOException
            or HttpRequestException { StatusCode: null };

    /// <summary>
    /// Summarizes what fills a context that is filling up: the earlier turns, kept as a history
    /// checkpoint, or — when they are already summarized or too small — the completed steps of a
    /// long turn in progress. False when neither was worth a summary or the model gave none.
    /// </summary>
    private async Task<bool> CompactAheadAsync(ToolRunContext run, AdaptiveCompactionBudget budget, CancellationToken token)
    {
        var minimum = budget.MinimumGainTokens;
        try
        {
            var preview = checkpoints.Preview(run, ContextCompactionScope.History);
            if (preview.CanCompact && preview.SourceTokens >= minimum
                && (await checkpoints.CompactAsync(run, budget.HistorySummaryTargetTokens, ContextCompactionScope.History, token,
                    HistoryCheckpointOrigin.Automatic, minimum)).Applied)
                return true;
            return (await checkpoints.CompactTurnAheadAsync(run, budget.HistoryKeepTokens, minimum,
                budget.SummaryTargetTokens, token)).Applied;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // A summary that could not be written leaves the request to the deterministic compaction.
            return false;
        }
    }

    /// <summary>
    /// How many times in a row an answer may be cut off at the token ceiling and asked to continue.
    /// Each continuation is progress — a truncated turn always produced a ceiling's worth of text —
    /// so this is not a budget for patience but a stop for the pathological case: a model that has
    /// started repeating itself would otherwise be paid to do so forever, and nobody is watching,
    /// because the whole point of continuing is that it happens without anyone being told.
    /// </summary>
    private const int MaxTruncatedTurns = 5;

    /// <summary>Steps in a row in which no call told the model anything new.</summary>
    private const int MaxStalledSteps = 3;
    private const string StalledAnswer =
        "I stopped after several steps produced no new information. The request may be incomplete. "
        + "Please review the tool results and provide missing information or a different approach.";

    /// <summary>
    /// Registered as a model-only system instruction after a truncated response. It never reaches
    /// persistence or the transcript and is removed after the next provider response is accepted.
    /// </summary>
    private const string ContinueAfterTruncation =
        "Your previous message was cut off at the output token limit. Continue it from exactly where "
        + "it stopped, in the middle of the word or line if that is where the cut fell. Do not repeat "
        + "any text you have already sent, do not restate what you were doing, and do not apologise.";

    /// <summary>
    /// The base prompt, project instructions and memory index, built once per run. Their order in
    /// the preview is their order in the prompt, so the priority is taken from it. A catalog that
    /// cannot be read leaves the run without its standing layers rather than failing every chat.
    /// </summary>
    private async Task UpsertStandingAsync(ToolRunContext run, bool appToolsAvailable,
        ConnectionSettings? connection, CancellationToken token)
    {
        ModelContextPreview preview;
        try
        {
            preview = await standingInstructions.BuildAsync(run.ProjectId, appToolsAvailable, token, connection);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            return;
        }
        for (var index = 0; index < preview.Layers.Count; index++)
        {
            if (preview.Layers[index].Content.Length == 0) continue;
            instructions.Upsert(run, new ModelInstruction(preview.Layers[index].Key, preview.Layers[index].Content,
                preview.Layers.Count - index, ModelInstructionLifetime.Run, ModelInstructionPlacement.Standing));
        }
    }

    /// <summary>
    /// Names the playbook the conversation is in, before every step. Its instructions sit in an
    /// earlier tool result, and a model that meets a follow-up — an answer to the playbook's
    /// question, "also do X" — without a word about it treats the message as a fresh request and
    /// improvises, or loads the playbook again from its first step. The same reminder says when to
    /// let go: a different task picks its own skill. Re-read each step, so a playbook loaded
    /// mid-run takes over at once; a catalog that cannot be read leaves the step without it.
    /// </summary>
    private async Task UpsertActiveSkillAsync(ToolRunContext run, IReadOnlyList<ChatCompletionMessage> context,
        CancellationToken token)
    {
        SkillDefinition? active;
        try
        {
            active = await skillGuide.ActivePlaybookAsync(run.ProjectId, context, token);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            active = null;
        }
        if (active is null)
        {
            instructions.Remove(run, ActiveSkillKey);
            return;
        }
        instructions.Upsert(run, new ModelInstruction(ActiveSkillKey,
            $"Active skill: {active.Id} ({active.Name}), a playbook loaded earlier in this conversation; its instructions "
            + "are in that mcp_app__run_skill result. While the user's latest message continues that task (an answer to its "
            + "question, a correction, a next step or an addition), keep following those instructions from the step you "
            + "reached, including their checks and final report; do not start over. Run it again only if its instructions "
            + "are no longer in the conversation. If the message is a different task, leave this skill and pick the one "
            + "from the skill catalog that fits the new task, or none.",
            // Trailing: it appears once a playbook is loaded and goes once the task moves on, and
            // either change would otherwise cost the cached conversation after it.
            880, ModelInstructionLifetime.Run, ModelInstructionPlacement.Trailing,
            CompactContent: $"Active skill: {active.Id}. Its playbook is in the earlier run_skill result. Continue it from the reached step "
                + "for follow-ups, corrections and additions; preserve its checks and final report. Reload only if the playbook is missing. "
                + "For a different task choose a matching skill again."));
    }

    private const string ActiveSkillKey = "run.active-skill";

    /// <summary>
    /// Shows the model its unfinished plan before every step. The plan's last result can be far back
    /// in a long turn, or compacted away, or in an earlier turn; without it the model forgets to move
    /// the plan, and the bar the user watches stops while the work goes on. A finished plan needs
    /// nothing more, and a follow-up on another task is told how to let it go.
    /// </summary>
    private void UpsertPlan(ToolRunContext run)
    {
        if (plans.Current(run) is not { } plan
            || plan.Steps.All(step => step.IsFinished) && ChatPlanCriteriaSummary.Of(plan).Unverified == 0)
        {
            instructions.Remove(run, PlanKey);
            return;
        }
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var progress = ChatPlanProgress.Of(plan, plans.Now(run).Clock);
        var text = new StringBuilder("Current plan")
            .Append(plan.Title is { } title ? $" \"{title}\"" : "")
            .Append(invariant, $", {progress.Percent}% done")
            .Append(progress.Remaining is { } left ? $", about {Took(left)} of work left" : "")
            .Append(" (mcp_app__plan_update):");
        string? over = null;
        for (var index = 0; index < plan.Steps.Count; index++)
        {
            var step = plan.Steps[index];
            var timing = progress.Steps[index];
            text.Append(invariant, $"\n{index + 1}. [{step.Status}] {step.Title} (weight {step.Weight}");
            if (step.Minutes is { } minutes) text.Append(invariant, $", estimated {minutes:0.#} min");
            if (step.Status == ChatPlanStepStatus.InProgress)
                text.Append(invariant, $", {step.Progress}%").Append(timing.Spent is { } spent ? $", {Took(spent)} so far" : "");
            text.Append(')');
            if (step.Note is { } note) text.Append(" - ").Append(note);
            if (step.Status == ChatPlanStepStatus.InProgress && timing is { IsOver: true, Spent: { } took, Expected: { } expected })
                over = $"Step {index + 1} has taken {Took(took)} against about {Took(expected)} expected. Say in a note what is "
                    + "holding it up and give it new minutes, or lay out a revised plan; if the approach itself is in doubt, "
                    + "ask the user before going on.";
        }
        if (plan.Criteria is { Count: > 0 } criteria)
        {
            text.Append("\nDone when:");
            for (var index = 0; index < criteria.Count; index++)
                text.Append(invariant, $"\n  {index + 1}. [{criteria[index].Status}] {criteria[index].Text}")
                    .Append(criteria[index].Evidence is { } evidence ? " - " + evidence : "");
        }
        text.Append("\nKeep it current as you work: mark a step InProgress when you start it, report the progress of a long "
            + "step, and mark it Completed as soon as it is done, or Skipped or Failed with a note. Several moves go in one call. "
            + "If the user's latest message is a different task, lay out a new plan for it, with its own criteria or "
            + "criteria [] for none, or skip the remaining steps. Settle each criterion with checks once you have checked "
            + "it, citing the tool call that showed it.");
        if (over is not null) text.Append('\n').Append(over);
        instructions.Upsert(run, new ModelInstruction(PlanKey, text.ToString(),
            // Trailing: the plan moves with the work, and a change here must not cost the cached conversation.
            875, ModelInstructionLifetime.Run, ModelInstructionPlacement.Trailing));
    }

    /// <summary>Whole minutes, or seconds under one: the model needs the scale, and finer would change every step.</summary>
    private static string Took(TimeSpan time) => time.TotalMinutes < 1
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(int)time.TotalSeconds}s")
        : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(int)Math.Round(time.TotalMinutes)} min");

    private const string PlanKey = "run.plan";

    private const string CriteriaKey = "run.plan-criteria";

    /// <summary>The criteria of the run's plan nobody has checked yet, numbered as the model refers to them.</summary>
    private string[] OpenCriteria(ToolRunContext run) => plans.Current(run)?.Criteria is { } criteria
        ? criteria.Select((criterion, index) => (criterion, index))
            .Where(item => item.criterion.Status == ChatPlanCriterionStatus.Unverified)
            .Select(item => $"{item.index + 1}. {item.criterion.Text}").ToArray()
        : [];

    private static string CriteriaGate(IReadOnlyList<string> open) =>
        "Before you answer: these criteria of the plan are still Unverified: " + string.Join("; ", open) + ". "
        + "Check each with a tool and settle it with mcp_app__plan_update checks - Met or NotMet, with the evidence and "
        + "the evidenceCall of the tool call that showed it - or mark it NotApplicable with the reason if it does not "
        + "concern this task. If one cannot be checked, leave it Unverified and say in your answer that it was not "
        + "verified and why. Then give your answer.";

    /// <summary>
    /// Asks the <c>skill-route</c> skill about the message that starts this turn, and pins the tools
    /// its first steps need. Routing that fails for any reason leaves the turn as it would have been
    /// without it.
    /// </summary>
    private async Task<SkillRoute?> RouteAsync(ToolRunContext run, IReadOnlyList<ChatCompletionMessage> context,
        IReadOnlyList<AgentTool> permitted, CancellationToken token)
    {
        SkillRoute? route;
        try
        {
            route = await skillRouting.RouteAsync(run, context, permitted, token);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException
                                          or ArgumentException or InvalidOperationException)
        {
            return null;
        }
        if (route is not null)
            toolCatalog.Pin(run, route.Tools.Concat(route.Skills.SelectMany(skill => skill.AllowedTools ?? [])));
        return route;
    }

    /// <summary>
    /// A playbook the route names first is loaded by the application rather than suggested: told in
    /// so many words to call mcp_app__run_skill first, models still asked their own questions and did the
    /// work by hand, and the playbook's later steps — the project's first chat, opening it — never
    /// happened. Loading one only returns its instructions, so it is done without asking. A playbook
    /// with required parameters is left to the model, which has to supply them.
    /// </summary>
    private static bool LoadsItself(SkillRoute route) =>
        route is { ContinuesActive: false, Skills: [{ Kind: SkillKinds.Playbook } first, ..] }
        && !(first.ParametersSchema.ValueKind == JsonValueKind.Object
             && first.ParametersSchema.TryGetProperty("required", out var required)
             && required.ValueKind == JsonValueKind.Array && required.GetArrayLength() > 0);

    /// <summary>
    /// Tells the model what routing decided. The hint lasts until the model's first answer; after
    /// that the active-skill reminder carries the choice.
    /// </summary>
    private void UpsertRouteHint(ToolRunContext run, SkillRoute route, bool loaded)
    {
        if (route.Skills.Count == 0) return;
        var first = route.Skills[0];
        var next = route.Skills.Count > 1
            ? $" When it is done, run {route.Skills[1].Id} for the rest of the request, unless the first skill hands "
              + "that part on to another chat."
            : "";
        var hint = route.ContinuesActive
            ? $"Skill routing: the user's latest message continues the active skill {first.Id}. Keep following its "
              + "instructions from the step you reached."
            : loaded
                ? $"Skill routing: the application loaded the skill {first.Id} for the user's latest message; its "
                  + "instructions are in the mcp_app__run_skill result just before this request. Follow them now from step 1, "
                  + "taking the values of its parameters from the user's message, and do every step, including the "
                  + "last ones. Only if the message plainly asks for something else, set the skill aside." + next
                : $"Skill routing: {string.Join("; then ", route.Skills.Select(skill => $"{skill.Id} ({skill.Description})"))} "
                  + $"fits the user's latest message. Unless the message plainly asks for something else, your first call is "
                  + $"mcp_app__run_skill with skillId {first.Id}, taking its parameters from the message. Do not ask questions or "
                  + "start the work before it: the skill says what to ask and how to do the work." + next;
        instructions.Upsert(run, new ModelInstruction(RouteKey, hint, 885, ModelInstructionLifetime.UntilAcknowledged));
    }

    private const string RouteKey = "run.skill-route";

    private const string RunSkillTool = "run_skill";

    /// <summary>
    /// The run-wide rule for ending a turn. A response without tool calls is the final answer, so a
    /// model that stops to announce its next step has ended the turn there; this says not to.
    /// </summary>
    private const string FinishingInstruction =
        "Keep working with tools until the request is done. For a substantial task, you may briefly orient the user "
        + "with your first tool calls. During work, write a short user-facing note only for a meaningful finding, "
        + "a consequential decision or change of approach, a blocker, or material progress in a long run. Explain "
        + "why it matters to the user's goal. Do not narrate routine searches, edits, checks or every tool call; "
        + "do not repeat tool output. Put any progress note in assistant content, in the user's language, alongside "
        + "the next tool calls, not in reasoning or a separate reply. "
        + "A reply without a tool call ends your turn and is shown to the user as your final answer, so a progress "
        + "note must not be sent as a standalone reply: include the tool calls and take the next step. Do not end by asking "
        + "permission to continue (\"Shall I…?\", \"Want me to…?\"): if the request already covers that step, do it. When a "
        + "decision is genuinely the user's, call ask_user; a question written in the reply ends your turn unanswered. "
        + "In the final answer, lead with the outcome in one or two plain sentences. Add only the few findings, "
        + "verification results and limitations needed to understand or use it; group related details and link to "
        + "sources instead of listing every step, file or command. Give a full report when the user asks for one or "
        + "the task's deliverable requires it. If blocked, state the remaining work and the specific blocker.";

    private const string CompactFinishingInstruction =
        "Complete the authorized request with tools. In assistant content alongside calls, briefly note only "
        + "meaningful findings, decisions, blockers or material progress in a long run, in the user's language. "
        + "Do not narrate routine calls. "
        + "A reply without calls is final: never use it to announce another step or ask permission to continue authorized work. "
        + "Use ask_user for decisions that need the user. Lead the final answer with the outcome, then only essential "
        + "evidence and limitations; give full detail when requested or required by the deliverable.";

    /// <summary>
    /// OpenAI-compatible endpoints report the ceiling as "length"; several report the Anthropic
    /// spelling instead, and a gateway in front of either may pass through whichever it received.
    /// </summary>
    private static ContextUsage ToUsage(ContextPlan plan, long answerTokens) => new(
        plan.ContextWindowTokens,
        plan.ContextWindowSource,
        plan.InstructionTokens,
        plan.ToolDefinitionTokens,
        Math.Max(0, plan.EstimatedInputTokens - plan.InstructionTokens) + answerTokens,
        plan.ReservedOutputTokens,
        plan.OverheadTokens,
        plan.WasCompacted,
        plan.OmittedMessages);

    private static bool Truncated(string? finishReason) =>
        finishReason is "length" or "max_tokens";

    /// <summary>
    /// The protocol name of the tool that puts a question to the person. Matched on the App server's
    /// own tool, never on a name alone: a third-party server calling something "ask_user" must not
    /// be able to buy itself an unlimited call by picking the right word.
    /// </summary>
    private const string AskUserTool = "ask_user";

    private static bool AsksTheUser(AgentTool tool) =>
        tool.ServerId == AppMcpServer.Id && tool.OriginalName is AskUserTool or "app_navigate";

    /// <summary>
    /// The turn's own 24-hour limit, which time spent waiting on a person does not count against.
    /// </summary>
    /// <remarks>
    /// The ceiling exists to stop a run that has gone wrong, and a run stopped at a confirmation
    /// card has not gone wrong — it is doing exactly what it should. Charging the person's reading
    /// time to it meant a turn could be killed while the only thing it was waiting for was the
    /// answer sitting on screen, and the longer they thought about it the likelier that became.
    /// So the budget is given back: the ceiling measures the work, not the waiting.
    /// </remarks>
    private sealed class TurnDeadline
    {
        private readonly CancellationTokenSource _source;
        private long _expires;

        public TurnDeadline(CancellationTokenSource source, TimeSpan budget)
        {
            _source = source;
            _expires = Environment.TickCount64 + (long)budget.TotalMilliseconds;
            Reschedule();
        }

        /// <summary>Anything else that stops while the turn waits for a person: the plan's working clock.</summary>
        public Func<IDisposable>? PersonWait { get; set; }

        public async Task<T> WhileWaitingForAPersonAsync<T>(Func<Task<T>> wait)
        {
            var started = Environment.TickCount64;
            using var paused = PersonWait?.Invoke();
            try
            {
                return await wait();
            }
            finally
            {
                _expires += Environment.TickCount64 - started;
                Reschedule();
            }
        }

        private void Reschedule()
        {
            var left = TimeSpan.FromMilliseconds(Math.Max(0, _expires - Environment.TickCount64));
            try
            {
                _source.CancelAfter(left);
            }
            catch (ObjectDisposedException)
            {
                // The turn ended while the last wait was unwinding. Nothing left to extend.
            }
        }
    }

    /// <summary>
    /// The longest any one call may run, however talkative it is. A tool that keeps reporting keeps
    /// its patience renewed, so without this a wedged loop that says so every second would never end.
    /// </summary>
    private static readonly TimeSpan MaxCallDuration = TimeSpan.FromHours(24);

    /// <summary>
    /// How long a call may stay silent. The policy timeout used to measure the call's whole
    /// duration, which cannot be set correctly for both audiences: the same number has to fit a
    /// file read and a fan-out of subtasks, and the fan-out lost — it was killed mid-flight with
    /// every subtask it had started thrown away, while its progress notifications said plainly that
    /// it was working. MCP allows resetting the timeout on progress for exactly this reason, so the
    /// number now means what it can mean for both: how long a tool may say nothing at all.
    /// </summary>
    private sealed class Patience
    {
        private readonly CancellationTokenSource _source;
        private readonly TimeSpan _silence;
        private readonly long _expires;

        public Patience(CancellationTokenSource source, TimeSpan silence, TimeSpan total)
        {
            _source = source;
            _silence = silence;
            _expires = Environment.TickCount64 + (long)total.TotalMilliseconds;
            Renew();
        }

        public void Renew()
        {
            var left = TimeSpan.FromMilliseconds(Math.Max(0, _expires - Environment.TickCount64));
            try
            {
                _source.CancelAfter(_silence < left ? _silence : left);
            }
            catch (ObjectDisposedException)
            {
                // Progress is forwarded fire-and-forget, so a last notification can arrive after the
                // call it belongs to has finished and disposed its source. There is nothing left to
                // extend, which is the answer rather than a problem.
            }
        }
    }

    /// <summary>A read started ahead of its turn in the batch, with what was checked before it started.</summary>
    private sealed record StartedRead(AgentTool Tool, string Arguments, Task<ToolCallResult> Result);

    /// <summary>
    /// A tool declared read-only may run with other reads once the independent policy check has
    /// already allowed it. The annotation never grants access; it only schedules an allowed call.
    /// A wait or a question still runs in sequence because its time belongs to an event or person.
    /// </summary>
    private static bool RunsAlongside(AgentTool tool) =>
        tool.Descriptor.Annotations?.ReadOnlyHint == true
        && tool.OriginalName != "trigger_wait"
        && !AsksTheUser(tool);

    /// <summary>
    /// Starts every call of the batch that may run alongside the others: a declared read-only call
    /// that its policy allows without confirmation and within its call limit. A batch with fewer
    /// than two such calls starts nothing here and runs in turn as before.
    /// </summary>
    private async Task<Dictionary<int, StartedRead>> StartReadsAsync(List<ChatToolCall> calls, int startIndex,
        IReadOnlyList<AgentTool> selectedTools, RefreshableToolSession? session, IReadOnlyDictionary<string, int> counts,
        Guid projectId, Guid chatId, Guid branchId, WorkspaceRunKey runKey, CancellationToken alongside,
        CancellationToken token)
    {
        var started = new Dictionary<int, StartedRead>();
        if (session is null || calls.Count - startIndex < 2) return started;
        var candidates = new List<(int Index, AgentTool Tool, string Arguments, EffectiveToolPolicy Policy)>();
        var planned = new Dictionary<string, int>(counts, StringComparer.Ordinal);
        for (var index = startIndex; index < calls.Count; index++)
        {
            var call = calls[index];
            if (selectedTools.SingleOrDefault(item => item.ModelDefinition.Name == call.Name) is not { } tool
                || !RunsAlongside(tool)) break;
            var count = planned[call.Name] = planned.GetValueOrDefault(call.Name) + 1;
            string arguments;
            try
            {
                arguments = session.ValidateArguments(tool, call.Arguments);
            }
            catch (Exception error) when (error is ArgumentException or JsonException)
            {
                // Its turn in the batch reports the error, and it remains a barrier to later calls.
                break;
            }
            var policy = await PolicyAsync(projectId, chatId, branchId, tool, arguments, token);
            if (policy.Decision != "Allow" || count > policy.MaxCalls) break;
            candidates.Add((index, tool, policy.Command?.Subject.Arguments ?? arguments, policy));
        }
        if (candidates.Count < 2) return started;
        foreach (var (index, tool, arguments, policy) in candidates)
        {
            await workspace.RecordIntentAsync(runKey, tool.Descriptor, arguments, token);
            started[index] = new StartedRead(tool, arguments, ReadAsync(session, tool, arguments, policy, alongside, token));
        }
        return started;
    }

    private async Task<ToolCallResult> ReadAsync(RefreshableToolSession session, AgentTool tool, string arguments,
        EffectiveToolPolicy policy, CancellationToken alongside, CancellationToken token)
    {
        // Off the caller's thread at once, so a server that does its work before its first await
        // cannot hold up the reads started after it.
        await Task.Yield();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(alongside);
        var patience = new Patience(timeout, TimeSpan.FromSeconds(policy.TimeoutSeconds + 2), MaxCallDuration);
        try
        {
            return await session.CallAsync(tool, arguments, new Progress<ToolProgress>(_ => patience.Renew()), timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && !alongside.IsCancellationRequested)
        {
            return Error($"The tool went silent for {policy.TimeoutSeconds} seconds. "
                + "Its effects may have occurred. Do not automatically repeat it.");
        }
    }

    private Task<EffectiveToolPolicy> PolicyAsync(Guid projectId, Guid chatId, Guid branchId, AgentTool tool,
        string arguments, CancellationToken token) =>
        policies.ResolveAsync(projectId, chatId, branchId, tool.ServerId, tool.OriginalName, tool.SchemaHash, arguments, token);

    // The history keeps the whole result, host metadata included; the model is sent a projection
    // without it, so a third-party server cannot smuggle anything into context through _meta.
    /// <summary>What the model is sent for a read whose result it already has from earlier in the turn.</summary>
    private static string RepeatedRead(string firstCallId) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            unchanged = true,
            sameAs = firstCallId,
            note = "The same call returned exactly this result earlier in this turn, in call " + firstCallId
                   + "; nothing has changed since. Use that result."
        });

    private ChatCompletionMessage ToolMessage(string callId, ToolCallResult result) =>
        new("tool", toolResultCodec.Write(result), ToolCallId: callId, ModelContent: result.ModelContent);

    private ToolCallResult Error(string message)
    {
        var content = new[] { ToolContent.OfText(message) };
        return new ToolCallResult(content, null, null, true, modelProjector.Project(content, null, true));
    }

    private ToolCallResult Text(string message)
    {
        var content = new[] { ToolContent.OfText(message) };
        return new ToolCallResult(content, null, null, false, modelProjector.Project(content, null, false));
    }

    private ToolCallResult ToolCallLimitError(string tool, int count, EffectiveToolPolicy policy)
    {
        var message = $"Tool call limit reached for '{tool}': call {count} exceeds the limit of {policy.MaxCalls} "
            + $"from the {policy.MaxCallsScope} policy. Only this tool is limited for the current run; "
            + "other available tools remain usable. Do not ask the user to continue solely because of this limit. "
            + "Continue with other tools or finish using the information already available.";
        var content = new[] { ToolContent.OfText(message) };
        var structured = JsonSerializer.SerializeToElement(new
        {
            code = "tool_call_limit_reached",
            tool,
            count,
            limit = policy.MaxCalls,
            scope = policy.MaxCallsScope,
            limitedToolOnly = true,
            otherToolsAvailable = true
        });
        return new ToolCallResult(content, structured, null, true,
            modelProjector.Project(content, structured, true));
    }

    /// <summary>
    /// Isolated, tool-free summarizer that reuses the same chat completion client as the run.
    /// The same model is asked to compress earlier turns so the planner can fall back without
    /// waiting on the model to be told to call <c>context_compact</c>; a tool list is never
    /// sent, so the summary cannot recurse into more tool calls.
    /// </summary>
    private sealed class CompletionClientSummarizer(IChatCompletionClient completion,
        ChatCompletionRequest originalRequest, ITokenUsageMeter usageMeter) : IContextSummarizer
    {
        public async Task<string> SummarizeAsync(string prompt, CancellationToken cancellationToken)
        {
            using var usageScope = usageMeter.Begin(new TokenUsageScope(TokenUsagePurpose.Compaction));
            return (await completion.CompleteAsync(originalRequest with
            {
                Message = prompt,
                ContextMessages = [new ChatCompletionMessage("user", prompt)],
                Tools = []
            }, cancellationToken)).Content;
        }
    }
}
