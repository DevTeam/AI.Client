namespace AI.Client.Application.Tools;

using Chat;
using Chats;
using Projects;
using Settings;
using Contracts.Chat;
using Contracts.Runs;
using Contracts.Settings;
using System.Text;
using System.Text.Json;

public sealed class ChatAgent(IChatCompletionClient completion, IToolSessionFactory sessions,
    IProjectService projects, IChatService chats, IGlobalSettingsRepository settings)
{
    public async Task RunAsync(Guid projectId, Guid chatId, ChatCompletionRequest request,
        Func<ChatCompletionMessage, CancellationToken, Task> persist,
        Func<string, CancellationToken, Task> text,
        Func<string?, CancellationToken, Task> activity,
        Func<AgentTool, string, long, CancellationToken, Task<ToolApprovalAction>> approve,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(60));
        var token = deadline.Token;
        var global = await settings.LoadAsync(token);
        var project = await projects.GetAsync(projectId, token) ?? throw new InvalidOperationException("Project not found.");
        var projectBinding = project.McpServers.SingleOrDefault(server => server.Id == DefaultMcpServer.Id);
        var enabled = global.McpServers.SingleOrDefault(server => server.Id == DefaultMcpServer.Id) is { Enabled: true, Policy: not "Deny" }
            && projectBinding is not { Enabled: false };
        await using var session = enabled ? await sessions.OpenAsync(token) : null;
        var context = request.ContextMessages?.ToList() ?? [new ChatCompletionMessage("user", request.Message)];
        var runStart = context.FindLastIndex(message => message.Role == "user");
        var counts = context.Skip(Math.Max(0, runStart)).SelectMany(message => message.ToolCalls ?? [])
            .GroupBy(call => call.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var seenIds = context.SelectMany(message => message.ToolCalls ?? []).Select(call => call.Id).ToHashSet(StringComparer.Ordinal);
        while (true)
        {
            var available = new List<ChatToolDefinition>();
            if (session is not null)
                foreach (var tool in session.Tools)
                    if ((await PolicyAsync(projectId, chatId, tool, token)).Decision != "Deny") available.Add(tool.Definition);
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
                return;
            }
            if (calls.Any(call => !seenIds.Add(call.Id)))
                throw new InvalidOperationException("Duplicate tool call IDs or excessive calls.");
            var assistant = new ChatCompletionMessage("assistant", content.ToString(), calls.ToArray());
            await persist(assistant, token); // Durable intent before any side effect.
            context.Add(assistant);
            foreach (var call in calls)
            {
                string result;
                try
                {
                    token.ThrowIfCancellationRequested();
                    var tool = session?.Tools.SingleOrDefault(item => item.Definition.Name == call.Name)
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
                    else if (policy.Decision == "Ask" && await approve(tool, arguments, policy.TimeoutSeconds, token) == ToolApprovalAction.Deny)
                        result = Error("The user denied this invocation. Do not retry it.");
                    else
                    {
                        await activity(call.Name, token);
                        var current = await PolicyAsync(projectId, chatId, tool, token);
                        if (current.Decision == "Deny") result = Error("Policy changed before execution. Submit a new invocation.");
                        else
                        {
                            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                            // Leave a short transport margin for process_run to report its own timeout.
                            timeout.CancelAfter(TimeSpan.FromSeconds(policy.TimeoutSeconds + 2));
                            result = await session.CallAsync(tool, arguments, timeout.Token);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    result = Error("Invocation interrupted or timed out. Its effects may have occurred. Do not automatically repeat it.");
                    var interrupted = new ChatCompletionMessage("tool", result, ToolCallId: call.Id);
                    await persist(interrupted, CancellationToken.None);
                    throw;
                }
                catch (Exception error) when (error is ArgumentException or JsonException)
                {
                    result = Error(error.Message);
                }
                catch (Exception)
                {
                    // A transport failure can happen after a side effect. Record uncertainty and stop.
                    await persist(new ChatCompletionMessage("tool", Error("Tool transport failed; outcome unknown. Do not automatically repeat it."), ToolCallId: call.Id), CancellationToken.None);
                    throw;
                }
                var message = new ChatCompletionMessage("tool", result, ToolCallId: call.Id);
                await persist(message, token);
                context.Add(message);
                await activity(null, token);
            }
        }
    }
    private async Task<EffectivePolicy> PolicyAsync(Guid projectId, Guid chatId, AgentTool tool, CancellationToken token)
    {
        var global = await settings.LoadAsync(token);
        var server = global.McpServers.SingleOrDefault(item => item.Id == tool.ServerId);
        var project = await projects.GetAsync(projectId, token) ?? throw new InvalidOperationException("Project not found.");
        var binding = project.McpServers.SingleOrDefault(item => item.Id == tool.ServerId);
        var projectPolicy = project.ToolPolicies.SingleOrDefault(item => item.ServerId == tool.ServerId
            && item.Name == tool.OriginalName && item.SchemaHash == tool.SchemaHash);
        var chat = await chats.GetAsync(projectId, chatId, token) ?? throw new InvalidOperationException("Chat not found.");
        var chatPolicy = chat.ToolPolicies?.SingleOrDefault(item => item.ServerId == tool.ServerId
            && item.Name == tool.OriginalName && item.SchemaHash == tool.SchemaHash);
        var globalPolicy = global.ToolPolicies.SingleOrDefault(item => item.ServerId == tool.ServerId
            && item.Name == tool.OriginalName && item.SchemaHash == tool.SchemaHash);
        var policyDecision = chatPolicy?.Decision ?? projectPolicy?.Decision ?? globalPolicy?.Decision ?? "Ask";
        var decision = server is not { Enabled: true } || server.Policy == "Deny" || binding is { Enabled: false } || policyDecision == "Deny"
            ? "Deny" : policyDecision == "Allow" ? "Allow" : "Ask";
        return new EffectivePolicy(
            decision,
            Math.Clamp(chatPolicy?.MaxCallsPerRun ?? projectPolicy?.MaxCallsPerRun ?? globalPolicy?.MaxCallsPerRun ?? 65535, 1, int.MaxValue),
            Math.Clamp(chatPolicy?.TimeoutSeconds ?? projectPolicy?.TimeoutSeconds ?? globalPolicy?.TimeoutSeconds ?? 120, 1, 600));
    }
    private static string Error(string message) => JsonSerializer.Serialize(new { isError = true, error = message });
    private sealed record EffectivePolicy(string Decision, int MaxCalls, long TimeoutSeconds);
}