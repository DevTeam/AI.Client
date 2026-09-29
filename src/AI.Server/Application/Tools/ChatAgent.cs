namespace AI.Application.Tools;

using Chat;
using Chats;
using Projects;
using Settings;
using Contracts.Chat;
using Contracts.Tools;
using Contracts.Runs;
using Contracts.Settings;
using Contracts.Workspace;
using Workspace;
using Instructions;
using Contracts.Instructions;
using System.Text;
using System.Text.Json;

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
    IToolDefinitionSelector toolSelector, IToolCatalogRegistry toolCatalog,
    IModelContentCheckpointService checkpoints, IModelInstructionRegistry instructions,
    IModelInstructionComposer instructionComposer, IModelInstructionDiagnostics instructionDiagnostics,
    IRunCompletionProtocol completionProtocol, IToolSearchDefinitionEnricher toolSearchEnricher,
    IStandingInstructions standingInstructions, IContextTokenEstimator estimator) : IChatAgent
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
        Func<ContextUsage, CancellationToken, Task>? contextUsage = null)
    {
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
        var project = await projects.GetAsync(projectId, token) ?? throw new InvalidOperationException("Project not found.");
        // Every server is gated the same way: enabled and not denied globally, and not switched off
        // for this project. A server that fails the test is never started, so nothing it could
        // offer reaches the model or costs a process.
        bool Enabled(Guid serverId) =>
            global.McpServers.SingleOrDefault(server => server.Id == serverId) is { Enabled: true, Policy: not "Deny" }
            && project.McpServers.SingleOrDefault(server => server.Id == serverId) is not { Enabled: false };
        var servers = new[] { DefaultMcpServer.Id, AppMcpServer.Id }.Where(Enabled).ToHashSet();
        var grants = project.DirectoryGrants
            .Select(grant => new ToolDirectoryGrant(grant.CanonicalRoot, grant.Recursive, grant.ToolNames)).ToArray();
        var run = new ToolRunContext(projectId, chatId, branchId, interactive);
        using var catalogScope = toolCatalog.Begin(run);
        using var instructionScope = instructions.Begin(run);
        instructions.Upsert(run, new ModelInstruction("run.completion-protocol", CompletionInstruction,
            1_000, ModelInstructionLifetime.Run));
        // Several app tools take the project and chat they act on as ids, and nothing else in the
        // context says which ones this run belongs to; a model left to guess reads lists to find them.
        if (servers.Contains(AppMcpServer.Id))
            instructions.Upsert(run, new ModelInstruction("run.context",
                $"This run: projectId {projectId}, chatId {chatId}, branchId {branchId}. The main branch id equals the chat id.",
                900, ModelInstructionLifetime.Run));
        await UpsertStandingAsync(run, servers.Contains(AppMcpServer.Id), token);
        using var checkpointScope = checkpoints.Begin(run, async (prompt, ct) =>
            (await completion.CompleteAsync(request with
            {
                Message = prompt,
                ContextMessages = [new ChatCompletionMessage("user", prompt)],
                Tools = []
            }, ct)).Content);
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
        var missingCompletion = 0;
        var invalidCompletion = false;
        var stalledSteps = 0;
        var observedResults = new HashSet<(string Name, string Arguments, string Result)>();
        // The latest prose the model gave while a completion decision was required. It is not
        // published at once, but it is the answer to fall back on if the model then stops answering.
        string? provisionalAnswer = null;
        var completionRequired = counts.Count > 0;
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
                    if ((await PolicyAsync(projectId, chatId, tool, token)).Decision != "Deny") permitted.Add(tool);
            permitted.Add(completionProtocol.Tool);
            toolCatalog.Update(run, permitted);
            var stalled = stalledSteps >= MaxStalledSteps;
            if (stalled)
                instructions.Upsert(run, new ModelInstruction("run.stalled",
                    "Several tool calls produced no new information. Call app_finish_run alone now: complete if done, "
                    + "or blocked with the partial result and limitation. Do not call another tool.",
                    970, ModelInstructionLifetime.Request));
            var completionToolForced = completionRequired && (empty > 0 || invalidCompletion || missingCompletion >= 2 || stalled);
            IReadOnlyList<AgentTool> requestTools = completionToolForced ? [completionProtocol.Tool] : permitted;
            var selection = toolSelector.Choose(configuredConnection, request.Message, modelContext, requestTools,
                toolCatalog.GetPinned(run));
            var selectedTools = toolSearchEnricher.Enrich(selection.Tools, permitted, selection.BudgetTokens);
            var available = selectedTools.Select(item => item.ModelDefinition).ToArray();
            contextDiagnostics.RecordToolSelection(request.Model, selection.AvailableCount, selectedTools.Count,
                selection.AvailableTokens, selection.SelectedTokens, selection.BudgetTokens);
            if (!completionToolForced && selection.AvailableCount > selectedTools.Count)
                instructions.Upsert(run, new ModelInstruction("run.tool-discovery",
                    ToolDiscoveryInstruction(selection.AvailableCount, selectedTools.Count),
                    990, ModelInstructionLifetime.Request));
            else instructions.Remove(run, "run.tool-discovery");
            var calls = new List<ChatToolCall>();
            var content = new StringBuilder();
            string? finish = null;
            var chunkCount = 0;
            var composition = instructionComposer.Compose(run, modelContext);
            instructionDiagnostics.RecordInstructions(request.Model, composition.Keys, composition.EstimatedTokens);
            var plan = await contextPlanner.PlanAsync(configuredConnection, request.Model, composition.Messages, available,
                new CompletionClientSummarizer(completion, request), SummaryTargetTokens, token);
            contextDiagnostics.Record(request.Model, plan, composition.Messages.Count, available.Length);
            lastPlan = plan;
            await Usage(plan);
            if (!plan.Fits) throw new ContextWindowExceededException(plan);
            await foreach (var chunk in completion.StreamAsync(
                               request with { ContextMessages = plan.Messages, Tools = available }, token))
            {
                chunkCount++;
                if (chunk.ToolCalls is { } received) calls.AddRange(received);
                if (chunk.FinishReason is { Length: > 0 } reason) finish = reason;
                if (chunk.Content.Length == 0) continue;
                content.Append(chunk.Content);
                await Draft(chunk.Content);
            }
            if (calls.Count == 0 && content.Length == 0)
            {
                // An endpoint that answers with nothing at all has not decided to stop — it has
                // failed to answer, and usually only this once. Nothing is persisted for an empty
                // turn. Keep every unacknowledged instruction: an empty provider response did not
                // act on it. The next request explicitly describes the missing protocol result;
                // after tools were used it also advertises only app_finish_run, so the endpoint has
                // one unambiguous way to say complete or blocked.
                var attempt = ++empty;
                instructionDiagnostics.RecordEmptyResponse(request.Model, attempt, finish, chunkCount,
                    completionRequired, completionToolForced);
                instructions.Upsert(run, new ModelInstruction("response.empty",
                    completionRequired
                        ? $"Your last response was empty. Call {completionProtocol.Tool.ModelDefinition.Name} with complete or blocked."
                        : "Your last response was empty. Answer or call a tool.",
                    980, ModelInstructionLifetime.UntilAcknowledged));
                if (attempt > MaxEmptyTurns && completionRequired && provisionalAnswer is { Length: > 0 } fallback)
                    return await FinishAsync(fallback);
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
            if (calls.Count == 0)
            {
                if (completionRequired && ++missingCompletion <= MaxMissingCompletionTurns)
                {
                    provisionalAnswer = continuedAnswer.ToString() + content;
                    context.Add(new ChatCompletionMessage("assistant", provisionalAnswer));
                    continuedAnswer.Clear();
                    instructions.Upsert(run, new ModelInstruction("run.completion-required",
                        // The user never saw that text, so a finalAnswer that points back at it
                        // ("see above") would publish a reference to nothing. Offering "continue" first
                        // read as a request for more work: models that had finished redid their last
                        // steps, or loaded the skill they had just completed and started it over.
                        $"Your previous message ended without a tool call, so it has not been published yet. If it answers "
                        + $"the request, call {completionProtocol.Tool.ModelDefinition.Name} with status complete and "
                        + "includePreviousText true to publish it as written; finalAnswer then adds only a closing line, if any. "
                        + "Do not retype it or replace it with a summary or a reference. Call an ordinary tool "
                        + "only if the request still needs work, and continue from the last step you finished: do not redo "
                        + "finished steps or start a skill over.",
                        950, ModelInstructionLifetime.UntilAcknowledged));
                    continue;
                }

                // Either no tool was used, or the model was asked for the control tool every time it
                // is allowed to be — the last requests offered nothing else — and still answered in
                // prose. Some OpenAI-compatible endpoints never emit a tool call for it. Failing the
                // run would throw away an answer the model has repeated several times, so its latest
                // text is published instead.
                var finalText = continuedAnswer.ToString() + content;
                continuedAnswer.Clear();
                return await FinishAsync(finalText);
            }

            var completionCalls = calls.Where(call => IsCompletionCall(call.Name)).ToArray();
            if (stalled && completionCalls.Length == 0)
                return await FinishAsync(StalledAnswer);
            if (completionCalls.Length > 0)
            {
                // Once the model has entered the structured completion protocol, every correction
                // stays in that protocol even if no ordinary tool preceded it.
                completionRequired = true;
                if (completionCalls.Length != 1 || calls.Count != 1)
                    throw new InvalidOperationException(
                        $"{completionProtocol.Tool.ModelDefinition.Name} must be the only call in its tool-call batch.");
                RunCompletionDecision decision;
                try
                {
                    decision = completionProtocol.Parse(completionCalls[0].Arguments);
                    if (decision is { IncludePreviousText: true, FinalAnswer.Length: 0 } && provisionalAnswer is not { Length: > 0 })
                        throw new ArgumentException("There is no unpublished previous text to include. Put the answer in finalAnswer.");
                }
                catch (Exception error) when (error is ArgumentException or JsonException)
                {
                    invalidCompletion = true;
                    if (++missingCompletion > MaxMissingCompletionTurns)
                        throw new InvalidOperationException(
                            $"The model repeatedly returned an invalid {completionProtocol.Tool.ModelDefinition.Name} decision.", error);
                    context.Add(new ChatCompletionMessage("assistant", content.ToString(), calls.ToArray()));
                    context.Add(new ChatCompletionMessage("tool", completionProtocol.RejectResult(error.Message),
                        ToolCallId: completionCalls[0].Id));
                    instructions.Upsert(run, new ModelInstruction("run.completion-invalid",
                        $"The previous {completionProtocol.Tool.ModelDefinition.Name} call was rejected. "
                        + "Correct its arguments using the tool result and retry; do not answer in prose.",
                        960, ModelInstructionLifetime.UntilAcknowledged));
                    continue;
                }
                missingCompletion = 0;
                // A playbook that ends "finish with one line" had the model publish that line and
                // drop the report it had just written as plain text. Including the held-back text
                // publishes it as it was, without asking the model to retype a long answer.
                return await FinishAsync(decision is { IncludePreviousText: true } && provisionalAnswer is { Length: > 0 } unpublished
                    ? decision.FinalAnswer.Length == 0 || unpublished.Contains(decision.FinalAnswer, StringComparison.Ordinal)
                        ? unpublished
                        : $"{unpublished}\n\n{decision.FinalAnswer}"
                    : decision.FinalAnswer);
            }

            completionRequired = true;
            missingCompletion = 0;
            invalidCompletion = false;
            // Held-back prose followed by more work is that work's preamble, and is published with
            // it. Dropping it lost whole reports: a playbook that renders a report and then asks
            // whether to save it went on to say "the report is above" about text nobody saw.
            var preamble = content.ToString();
            if (provisionalAnswer is { Length: > 0 } heldBack
                && context is [.., { Role: "assistant", ToolCalls: null } last] && last.Content == heldBack)
            {
                context.RemoveAt(context.Count - 1);
                preamble = preamble.Length == 0 ? heldBack : $"{heldBack}\n\n{preamble}";
            }
            provisionalAnswer = null;
            continuedAnswer.Clear();
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
                        ?? throw new ArgumentException(permitted.Any(item => item.ModelDefinition.Name == call.Name)
                            ? $"Tool '{call.Name}' is not available in this turn. Its schema was omitted to fit the model's context budget. "
                              + "Call app_tool_search with a short English capability description (for example: 'read text file', 'list directory', 'grep in files') "
                              + "so the matching tools are pinned and become available on the next model step. Do not invent or guess tool names."
                            : $"There is no tool named '{call.Name}'. Use only the tool names you were given; call app_tool_search "
                              + "with a short English capability description when the one you need is not listed.");
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
    /// Token budget for the LLM-generated summary used as a last-resort compaction step. Small
    /// enough to leave room for instructions, tools and the recent turn in the same window, large
    /// enough to keep enough decisions to continue the work.
    /// </summary>
    private const int SummaryTargetTokens = 1500;

    /// <summary>
    /// How many times in a row an answer may be cut off at the token ceiling and asked to continue.
    /// Each continuation is progress — a truncated turn always produced a ceiling's worth of text —
    /// so this is not a budget for patience but a stop for the pathological case: a model that has
    /// started repeating itself would otherwise be paid to do so forever, and nobody is watching,
    /// because the whole point of continuing is that it happens without anyone being told.
    /// </summary>
    private const int MaxTruncatedTurns = 5;

    /// <summary>
    /// A compatible model should follow the control protocol immediately. A few corrective turns
    /// tolerate providers which initially emit prose, while still preventing an endless private
    /// conversation whose result can never be published.
    /// </summary>
    private const int MaxMissingCompletionTurns = 3;

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
    /// Whether a call is the completion control tool. The control tool has no server prefix, but a
    /// model that has just read App tool names sometimes gives it the App server's; that spelling is
    /// accepted too. Only the App prefix is: a third-party server could otherwise end a run by
    /// naming one of its own tools app_finish_run.
    /// </summary>
    private bool IsCompletionCall(string name) =>
        name == completionProtocol.Tool.ModelDefinition.Name
        || name == ToolRef.AppPrefix + completionProtocol.Tool.ModelDefinition.Name;

    /// <summary>
    /// The base prompt, project instructions and memory index, built once per run. Their order in
    /// the preview is their order in the prompt, so the priority is taken from it. A catalog that
    /// cannot be read leaves the run without its standing layers rather than failing every chat.
    /// </summary>
    private async Task UpsertStandingAsync(ToolRunContext run, bool appToolsAvailable, CancellationToken token)
    {
        ModelContextPreview preview;
        try
        {
            preview = await standingInstructions.BuildAsync(run.ProjectId, appToolsAvailable, token);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            return;
        }
        for (var index = 0; index < preview.Layers.Count; index++)
            instructions.Upsert(run, new ModelInstruction(preview.Layers[index].Key, preview.Layers[index].Content,
                preview.Layers.Count - index, ModelInstructionLifetime.Run, ModelInstructionPlacement.Standing));
    }

    /// <summary>
    /// The stable run-wide protocol. Ordinary text before a tool call remains a compact
    /// intermediate note; only finalAnswer from this control tool can become the durable final
    /// answer once the run has used a side-effect or information-gathering tool.
    /// </summary>
    private const string CompletionInstruction =
        "After using a tool, ordinary text is provisional. Keep working with ordinary tools. To finish, call "
        + "app_finish_run alone with complete if the request is satisfied, or blocked if available information or "
        + "tools cannot support further progress or a reliable answer. For blocked, explain any partial result and "
        + "the limitation in finalAnswer. The user sees only finalAnswer, so it must contain the complete answer, "
        + "never a reference to earlier text.";

    private static string ToolDiscoveryInstruction(int availableCount, int selectedCount) =>
        $"Only {selectedCount} of {availableCount} permitted tools are shown. If a needed capability is missing, "
        + "call app_tool_search with a short English description before concluding it is unavailable. "
        + "Call only tools shown in this request; search results become callable on the next step.";

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
        tool.ServerId == AppMcpServer.Id && tool.OriginalName == AskUserTool;

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
        ChatCompletionRequest originalRequest) : IContextSummarizer
    {
        public async Task<string> SummarizeAsync(string prompt, CancellationToken cancellationToken) =>
            (await completion.CompleteAsync(originalRequest with
            {
                Message = prompt,
                ContextMessages = [new ChatCompletionMessage("user", prompt)],
                Tools = []
            }, cancellationToken)).Content;
    }
}
