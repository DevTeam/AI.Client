namespace AI.Client.Application.Tools;

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
    IProjectService projects, IGlobalSettingsRepository settings, ToolPolicyResolver policies,
    IWorkspaceChangeTracker workspace)
{
    public async Task<WorkspaceChangeSet> RunAsync(Guid projectId, Guid chatId, Guid branchId, ChatCompletionRequest request,
        Func<ChatCompletionMessage, CancellationToken, Task> persist,
        Func<string, CancellationToken, Task> text,
        Func<ToolActivity?, CancellationToken, Task> activity,
        Func<AgentTool, string, long, ToolCallPosition, CancellationToken, Task<ToolApprovalAction>> approve,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(60));
        var token = deadline.Token;
        var global = await settings.LoadAsync(token);
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
        await using var session = servers.Count > 0 ? await sessions().OpenAsync(grants, servers, token) : null;
        var runKey = new WorkspaceRunKey(projectId, chatId, branchId);
        await workspace.BeginRunAsync(runKey, grants, token);
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

        while (true)
        {
            var available = new List<ChatToolDefinition>();
            if (session is not null)
                foreach (var tool in session.Tools)
                    if ((await PolicyAsync(projectId, chatId, tool, token)).Decision != "Deny") available.Add(tool.ModelDefinition);
            var calls = new List<ChatToolCall>();
            var content = new StringBuilder();
            await foreach (var chunk in completion.StreamAsync(request with { ContextMessages = context, Tools = available }, token))
            {
                if (chunk.ToolCalls is { } received) calls.AddRange(received);
                if (chunk.Content.Length == 0) continue;
                content.Append(chunk.Content);
                await text(chunk.Content, token);
            }
            if (calls.Count == 0)
            {
                if (content.Length == 0) throw new InvalidOperationException("The model returned an empty response.");
                var changes = await workspace.SnapshotAsync(runKey, token);
                await workspace.CompleteRunAsync(runKey, CancellationToken.None);
                return changes;
            }
            if (calls.Any(call => !seenIds.Add(call.Id)))
                throw new InvalidOperationException("Duplicate tool call IDs or excessive calls.");
            var assistant = new ChatCompletionMessage("assistant", content.ToString(), calls.ToArray());
            await persist(assistant, token); // Durable intent before any side effect.
            context.Add(assistant);
            for (var index = 0; index < calls.Count; index++)
            {
                var call = calls[index];
                ToolCallResult result;
                try
                {
                    token.ThrowIfCancellationRequested();
                    var tool = session?.Tools.SingleOrDefault(item => item.ModelDefinition.Name == call.Name)
                        ?? throw new ArgumentException("Unknown tool.");
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
                    else if (count > policy.MaxCalls) result = Error("Tool call limit reached.");
                    else if (policy.Decision == "Ask"
                             && await approve(tool, arguments, policy.TimeoutSeconds,
                                 new ToolCallPosition(index + 1, calls.Count), token) == ToolApprovalAction.Deny)
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
                            // Leave a short transport margin for process_run to report its own timeout.
                            timeout.CancelAfter(TimeSpan.FromSeconds(policy.TimeoutSeconds + 2));
                            // Progress arrives on the transport's own thread while the call is in
                            // flight, so it is forwarded fire-and-forget: a slow subscriber must
                            // not be able to stall the tool it is reporting on.
                            var progress = new Progress<ToolProgress>(update => _ = activity(
                                activeCall with { Progress = update.Progress, Total = update.Total, Message = update.Message },
                                CancellationToken.None));
                            // The baseline has to exist before the call, not after: once a write
                            // lands there is nothing left to compare against.
                            await workspace.RecordIntentAsync(runKey, tool.Descriptor, arguments, token);
                            try
                            {
                                result = await session.CallAsync(tool, arguments, progress, timeout.Token);
                            }
                            // A call that outlived its own policy timeout has failed; the turn has
                            // not. Letting that reach the outer handler abandoned every call after
                            // it and ended the run, so one slow server took the whole batch with
                            // it. The run's own cancellation still does exactly that, which is the
                            // difference this guard draws.
                            catch (OperationCanceledException) when (!token.IsCancellationRequested)
                            {
                                result = Error($"The tool did not answer within {policy.TimeoutSeconds} seconds. "
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
            }
        }
    }
    private Task<EffectiveToolPolicy> PolicyAsync(Guid projectId, Guid chatId, AgentTool tool, CancellationToken token) =>
        policies.ResolveAsync(projectId, chatId, tool.ServerId, tool.OriginalName, tool.SchemaHash, token);

    // The history keeps the whole result, host metadata included; the model is sent a projection
    // without it, so a third-party server cannot smuggle anything into context through _meta.
    private static ChatCompletionMessage ToolMessage(string callId, ToolCallResult result) =>
        new("tool", ToolResultCodec.Write(result), ToolCallId: callId, ModelContent: result.ModelContent);

    private static ToolCallResult Error(string message) => ToolCallResult.FromError(message);
}
