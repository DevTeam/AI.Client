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
using Contracts.Usage;
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
    IClock clock, IIdGenerator ids, IChatKindPolicyRegistry kindPolicies) : IChatAgent
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
        bool overlayPromptsAllowed = true)
    {
        var estimator = tokenEstimator.ForModel(request.Model);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var transportScope = transport.BeginScope(transportActivity);
        var deadline = new TurnDeadline(source, TimeSpan.FromMinutes(60));
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
        if (configuredConnection is not null) configuredConnection = configuredConnection with { Model = request.Model };
        var project = await projects.GetAsync(projectId, token) ?? throw new InvalidOperationException("Project not found.");
        // Every server is gated the same way: enabled and not denied globally, and not switched off
        // for this project. A server that fails the test is never started, so nothing it could
        // offer reaches the model or costs a process.
        bool Enabled(Guid serverId) =>
            global.McpServers.SingleOrDefault(server => server.Id == serverId) is { Enabled: true, Policy: not "Deny" }
            && project.McpServers.SingleOrDefault(server => server.Id == serverId) is not { Enabled: false };
        var kindPolicy = kindPolicies.Resolve(request.Kind == default ? ChatKind.Conversation : request.Kind);
        kindPolicy.ValidateState(request.KindState, request.KindStateVersion);
        var behavior = kindPolicy.Behavior;
        var servers = global.McpServers.Select(server => server.Id).Where(Enabled)
            .Where(kindPolicy.AllowsServer).ToHashSet();
        var grants = project.DirectoryGrants.Where(_ => behavior.AllowDirectoryGrants)
            .Select(grant => new ToolDirectoryGrant(grant.CanonicalRoot, grant.Recursive, grant.ToolNames)).ToArray();
        var run = new ToolRunContext(projectId, chatId, branchId, interactive, kindPolicy.Kind, request.KindState,
            request.KindStateVersion, overlayPromptsAllowed);
        using var catalogScope = toolCatalog.Begin(run);
        using var instructionScope = instructions.Begin(run);
        instructions.Upsert(run, new ModelInstruction("run.finishing", behavior.FinishingInstruction ?? FinishingInstruction,
            1_000, ModelInstructionLifetime.Run, Required: true,
            CompactContent: behavior.CompactFinishingInstruction ?? (behavior.FinishingInstruction is null ? CompactFinishingInstruction : null)));
        // Several app tools take the project and chat they act on as ids, and nothing else in the
        // context says which ones this run belongs to; a model left to guess reads lists to find them.
        if (servers.Contains(AppMcpServer.Id))
            instructions.Upsert(run, new ModelInstruction("run.context",
                $"This run: projectId {projectId}, chatId {chatId}, branchId {branchId}. The main branch id equals the chat id.",
                900, ModelInstructionLifetime.Run, Required: true));
        if (behavior.IncludeStandingInstructions) await UpsertStandingAsync(run, servers.Contains(AppMcpServer.Id), configuredConnection, token);
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
        var runKey = new WorkspaceRunKey(projectId, chatId, branchId);
        await workspace.BeginRunAsync(runKey, grants,
            parentBranchId is { } parent ? new WorkspaceRunKey(projectId, chatId, parent) : null, token);
        async Task<WorkspaceChangeSet> FinishAsync(string answer)
        {
            await Draft(null);
            await Answer(answer);
            var changes = await workspace.SnapshotAsync(runKey, token);
            await workspace.CompleteRunAsync(runKey, CancellationToken.None);
            return changes;
        }
        var context = request.ContextMessages?.ToList() ?? [new ChatCompletionMessage("user", request.Message)];
        var runStart = context.FindLastIndex(message => message.Role == "user");
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
        var truncated = 0;
        var continuedAnswer = new StringBuilder();
        var stalledSteps = 0;
        var observedResults = new HashSet<(string Name, string Arguments, string Result)>();
        var routed = false;
        // What the request carried in the previous step: kept, so the next request starts the same.
        var compactionMemory = new ContextCompactionMemory();
        IReadOnlyList<AgentTool>? sentTools = null;
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
            var permitted = new List<AgentTool>();
            if (session is not null)
                foreach (var tool in session.Tools)
                    if (kindPolicy.AllowsTool(tool.ServerId, tool.OriginalName)
                        && (await PolicyAsync(projectId, chatId, tool, token)).Decision != "Deny") permitted.Add(tool);
            if (behavior.PinnedTools is { Count: > 0 } pinnedTools) toolCatalog.Pin(run, pinnedTools);
            toolCatalog.Update(run, permitted);
            // Once per turn, before its first step: which skill fits the new message, and which tools
            // the first steps need. The tools are pinned before the selector cuts the list.
            if (!routed)
            {
                routed = true;
                if (interactive && behavior.RouteSkills && servers.Contains(AppMcpServer.Id)
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
            if (behavior.UseActiveSkills && servers.Contains(AppMcpServer.Id))
                await UpsertActiveSkillAsync(run, context, token);
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
            }
            await Usage(plan);
            if (!plan.Fits) throw new ContextWindowExceededException(plan);
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
                var finalText = continuedAnswer.ToString() + content;
                continuedAnswer.Clear();
                return await FinishAsync(finalText);
            }

            // Told to stop calling tools, the model called one anyway: the loop ends here rather than
            // going round again.
            if (stalled) return await FinishAsync(StalledAnswer);

            continuedAnswer.Clear();
            var preamble = content.ToString();
            if (calls.Any(call => !seenIds.Add(call.Id)))
                throw new InvalidOperationException("Duplicate tool call IDs or excessive calls.");
            var assistant = new ChatCompletionMessage("assistant", preamble, calls.ToArray());
            // Cleared first so the publication that carries the preamble also drops its draft:
            // the text changes owner without a frame showing it twice or not at all.
            await Draft(null);
            await persist(assistant, token); // Durable intent before any side effect.
            context.Add(assistant);
            checkpoints.Update(run, context);
            for (var index = 0; index < calls.Count; index++)
            {
                var call = calls[index];
                ToolCallResult result;
                try
                {
                    token.ThrowIfCancellationRequested();
                    var tool = selectedTools.SingleOrDefault(item => item.ModelDefinition.Name == call.Name)
                        ?? throw new ArgumentException(toolGuidance.ForUnavailableCall(call.Name, selectedTools, permitted));
                    var arguments = session!.ValidateArguments(tool, call.Arguments);
                    var policy = await PolicyAsync(projectId, chatId, tool, token);
                    if (tool.OriginalName == "process_run")
                    {
                        var input = System.Text.Json.Nodes.JsonNode.Parse(arguments)!;
                        input["timeoutMs"] = Math.Min(input["timeoutMs"]?.GetValue<int>() ?? 120000, (int)policy.TimeoutSeconds * 1000);
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
                        var current = await PolicyAsync(projectId, chatId, tool, token);
                        if (current.Decision == "Deny") result = Error("Policy changed before execution. Submit a new invocation.");
                        else
                        {
                            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                            // A tool that is waiting on a person is not a tool that has gone quiet.
                            // Both clocks are off while it waits: the silence timer, which would
                            // kill the question in under a minute, and the turn's own hour, which
                            // must not be spent on time the person took to read it.
                            var asking = AsksTheUser(tool);
                            // Leave a short transport margin for process_run to report its own timeout.
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
                            catch (OperationCanceledException) when (!token.IsCancellationRequested)
                            {
                                result = Error($"The tool went silent for {policy.TimeoutSeconds} seconds. "
                                    + "Its effects may have occurred. Do not automatically repeat it.");
                            }

                            await workspace.RecordEffectAsync(runKey, tool.Descriptor, arguments, token);
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
                var message = ToolMessage(call.Id, result);
                await persist(message, token);
                context.Add(message);
                await activity(null, token);
                if (!result.IsError && observedResults.Add((call.Name, call.Arguments, result.ModelContent)))
                    stalledSteps = 0;
                else
                    stalledSteps++;
            }
        }
    }
    /// <summary>
    /// How many times a turn that produced nothing at all is asked again before the run gives up.
    /// Small on purpose: an endpoint that is genuinely answering nothing should be reported, not
    /// hammered, and the user is the one waiting through every attempt.
    /// </summary>
    private const int MaxEmptyTurns = 2;

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

    private const int MaxStalledSteps = 4;
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
        "Keep working with tools until the request is done. Before your first tool calls, write a brief "
        + "user-facing progress note in the assistant message's content explaining what you will do. During longer "
        + "work, include another short note with your next tool calls when you have a meaningful finding, change "
        + "of approach or have worked for about a minute since the last update. Use the user's language and describe "
        + "concrete progress; do not repeat tool output or narrate every call. These notes must be in content, "
        + "not just reasoning, and must accompany the tool calls in the same response. "
        + "A reply without a tool call ends your turn and is shown to the user as your final answer, so a progress "
        + "note must not be sent as a standalone reply: include the tool calls and take the next step. Do not end by asking "
        + "permission to continue (\"Shall I…?\", \"Want me to…?\"): if the request already covers that step, do it. When a "
        + "decision is genuinely the user's, call ask_user; a question written in the reply ends your turn unanswered. "
        + "End with the complete answer. If information or tools are missing, say what you did, what is left and what "
        + "blocks it.";

    private const string CompactFinishingInstruction =
        "Complete the authorized request with tools. Before the first calls and about every minute of longer work, "
        + "include a brief progress note in assistant content with the next tool calls, in the user's language. "
        + "A reply without calls is final: never use it to announce another step or ask permission to continue authorized work. "
        + "Use ask_user for decisions that need the user. Finish with the complete result, or verified progress and the remaining blocker.";

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
    /// The turn's own hour, which time spent waiting on a person does not count against.
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

        public async Task<T> WhileWaitingForAPersonAsync<T>(Func<Task<T>> wait)
        {
            var started = Environment.TickCount64;
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
    private static readonly TimeSpan MaxCallDuration = TimeSpan.FromMinutes(30);

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

    private Task<EffectiveToolPolicy> PolicyAsync(Guid projectId, Guid chatId, AgentTool tool, CancellationToken token) =>
        policies.ResolveAsync(projectId, chatId, tool.ServerId, tool.OriginalName, tool.SchemaHash, token);

    // The history keeps the whole result, host metadata included; the model is sent a projection
    // without it, so a third-party server cannot smuggle anything into context through _meta.
    private ChatCompletionMessage ToolMessage(string callId, ToolCallResult result) =>
        new("tool", toolResultCodec.Write(result), ToolCallId: callId, ModelContent: result.ModelContent);

    private ToolCallResult Error(string message)
    {
        var content = new[] { ToolContent.OfText(message) };
        return new ToolCallResult(content, null, null, true, modelProjector.Project(content, null, true));
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
