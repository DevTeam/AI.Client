namespace AI.Application.Skills;

using System.Text;
using System.Text.Json;
using AI.Application.Chat;
using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Settings;
using AI.Contracts.Chats;
using Microsoft.Extensions.Logging;

/// <summary>
/// One model call without tools that judges one pending tool call for manual and automatic approval
/// modes. It only ever answers; the caller decides, and treats anything but a well-formed
/// <c>allow</c> as a reason to ask the person.
/// </summary>
public sealed class ChatToolRiskAssessSkill(
    ISkillCatalog catalog,
    IChatService chats,
    IProjectService projects,
    IGlobalSettingsRepository settings,
    IGlobalSecretStore secrets,
    IChatCompletionClient completion,
    ILogger<ChatToolRiskAssessSkill> logger) : ISkillExecutor
{
    public const string Id = "chat-tool-risk-assess";

    public string SkillId => Id;

    private const int MaxReasonLength = 240;

    private static readonly Action<ILogger, Guid, Exception?> SkillFailed =
        LoggerMessage.Define<Guid>(LogLevel.Warning, new EventId(1604, "ChatToolRiskAssessSkillFailed"),
            "Chat tool risk assessment failed for chat {ChatId}");

    public async Task<SkillExecutionResult> RunAsync(SkillInvocation invocation, CancellationToken cancellationToken)
    {
        var parameters = invocation.Parameters;
        var target = parameters.GetProperty("chat_id").GetString()!;
        var chatId = target == "current"
            ? invocation.CurrentChatId ?? throw new ArgumentException("This skill run has no current chat.")
            : Guid.Parse(target);
        Guid? branchId = Text(parameters, "branch_id") is { } branch ? Guid.Parse(branch)
            : target == "current" ? invocation.CurrentBranchId : null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Someone may be watching the run wait on this, so it gets less time than a generic skill.
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            return await RunCoreAsync(invocation, chatId, branchId ?? chatId, deadline.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new SkillExecutionResult("Cancelled", "The risk assessment was cancelled.", chatId);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return new SkillExecutionResult("Failed", "The risk assessment timed out.", chatId);
        }
        catch (Exception error)
        {
            SkillFailed(logger, chatId, error);
            return new SkillExecutionResult("Failed", error.Message, chatId);
        }
    }

    private async Task<SkillExecutionResult> RunCoreAsync(SkillInvocation invocation, Guid chatId, Guid branchId,
        CancellationToken token)
    {
        var projectId = invocation.ProjectId;
        var skill = await catalog.GetByIdAsync(invocation.SkillId, projectId, token);
        if (skill is not { Enabled: true }) return new("Failed", "The risk assessment skill is unavailable.", chatId);
        var chat = await chats.GetAsync(projectId, chatId, token);
        if (chat is null) return new("Failed", "Chat not found in this project.", chatId);
        var project = await projects.GetAsync(projectId, token);

        var global = await settings.LoadAsync(token);
        var connectionId = chat.ConnectionId ?? project?.ConnectionId;
        var connection = global.Connections.FirstOrDefault(item => item.Id == connectionId && item.Enabled)
            ?? global.Connections.FirstOrDefault(item => item.IsDefault && item.Enabled)
            ?? global.Connections.FirstOrDefault(item => item.Enabled);
        if (connection is null) return new("Failed", "No enabled model connection is available.", chatId);

        var parameters = invocation.Parameters;
        var conversation = new List<ChatCompletionMessage>
        {
            new("system", SkillMarkdown.Body(skill.Content)),
            new("user", JsonSerializer.Serialize(new
            {
                user_request = LatestUserRequest(chat, branchId) is { } intent ? Clip(intent, 4_000) : null,
                project_directories = project?.DirectoryGrants.Select(grant => new
                {
                    path = grant.CanonicalRoot,
                    recursive = grant.Recursive
                }).ToArray() ?? [],
                tool = new
                {
                    name = Text(parameters, "tool_name"),
                    title = Text(parameters, "tool_title"),
                    description = Text(parameters, "tool_description") is { } description ? Clip(description, 2_000) : null,
                    annotations = Json(Text(parameters, "annotations"))
                },
                arguments = Clip(Text(parameters, "arguments") ?? "{}", 12_000)
            }))
        };
        var request = new ChatCompletionRequest(connection.BaseUrl, connection.Model,
            await secrets.GetAsync("connection", connection.Id, token),
            "Assess a tool call", connection.Id, conversation);
        var content = new StringBuilder();
        await foreach (var chunk in completion.StreamAsync(request, token)) content.Append(chunk.Content);
        if (Parse(content.ToString()) is not { } verdict)
            return new("Failed", "The model returned no usable assessment.", chatId);
        return new("Completed", $"Assessed the call: {verdict.Decision}.", chatId,
            Output: JsonSerializer.SerializeToElement(new { decision = verdict.Decision, risk = verdict.Risk, reason = verdict.Reason }));
    }

    /// <summary>What the person asked for last on this branch: the intent a call has to serve to be "plainly asked for".</summary>
    private static string? LatestUserRequest(ChatDetails chat, Guid branchId)
    {
        var byId = chat.Messages.ToDictionary(item => item.Id);
        var head = chat.Branches?.SingleOrDefault(item => item.Id == branchId)?.HeadMessageId
            ?? (chat.Messages.Count > 0 ? chat.Messages[^1].Id : null);
        for (var current = head is { } id && byId.TryGetValue(id, out var start) ? start : null;
             current is not null;
             current = current.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent) ? parent : null)
            if (current.Role == "User" && !string.IsNullOrWhiteSpace(current.Content)) return current.Content;
        return null;
    }

    private static (string Decision, string Risk, string? Reason)? Parse(string content)
    {
        var text = content.Trim();
        // Models wrap JSON in a fence often enough that refusing it would only mean asking more.
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var document = JsonDocument.Parse(text[start..(end + 1)]);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var decision = Text(root, "decision")?.Trim().ToLowerInvariant();
            if (decision is not ("allow" or "ask")) return null;
            var risk = Text(root, "risk")?.Trim().ToLowerInvariant() switch
            {
                "low" => "low",
                "medium" => "medium",
                "high" => "high",
                _ => "unknown"
            };
            // An allow that does not also call the risk low contradicts itself; the safe reading wins.
            if (decision == "allow" && risk != "low") decision = "ask";
            var reason = Text(root, "reason")?.Trim();
            return (decision, risk, reason is { Length: > 0 } ? Clip(reason, MaxReasonLength) : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Passed on as JSON rather than as a string of it, so the model reads the hints, not their escaping.</summary>
    private static JsonElement? Json(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return JsonSerializer.Deserialize<JsonElement>(text); }
        catch (JsonException) { return JsonSerializer.SerializeToElement(Clip(text, 500)); }
    }

    private static string Clip(string text, int length) => text.Length <= length ? text : text[..length] + "…";
}
