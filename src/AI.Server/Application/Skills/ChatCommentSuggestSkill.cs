namespace AI.Application.Skills;

using System.Text;
using System.Text.Json;
using AI.Application.Chat;
using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Settings;
using Microsoft.Extensions.Logging;

/// <summary>
/// One model call without tools: the fragment the user is commenting goes in with what surrounds
/// it — the message it is from, or the diff around the lines — and a draft of the comment comes
/// out. Nothing is changed; the draft is handed back as the run's output.
/// </summary>
public sealed class ChatCommentSuggestSkill(
    ISkillCatalog catalog,
    IChatService chats,
    IProjectService projects,
    IGlobalSettingsRepository settings,
    IGlobalSecretStore secrets,
    IChatCompletionClient completion,
    ILogger<ChatCommentSuggestSkill> logger) : ISkillExecutor
{
    public string SkillId => "chat-comment-suggest";

    private const int MaxLength = 300;
    private const int ContextLength = 6_000;

    // Code and non-Latin text go to the model as they are, not as \u escapes that cost tokens.
    private static readonly JsonSerializerOptions Prompt =
        new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly Action<ILogger, Guid, Exception?> SkillFailed =
        LoggerMessage.Define<Guid>(LogLevel.Warning, new EventId(1603, "ChatCommentSuggestSkillFailed"),
            "Chat comment suggestion skill failed for chat {ChatId}");

    public async Task<SkillExecutionResult> RunAsync(SkillInvocation invocation, CancellationToken cancellationToken)
    {
        var parameters = invocation.Parameters;
        var target = parameters.GetProperty("chat_id").GetString()!;
        var chatId = target == "current"
            ? invocation.CurrentChatId ?? throw new ArgumentException("This skill run has no current chat.")
            : Guid.Parse(target);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            return await RunCoreAsync(invocation, chatId, deadline.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new SkillExecutionResult("Cancelled", "The comment suggestion was cancelled.", chatId);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return new SkillExecutionResult("Failed", "The comment suggestion timed out.", chatId);
        }
        catch (Exception error)
        {
            SkillFailed(logger, chatId, error);
            return new SkillExecutionResult("Failed", error.Message, chatId);
        }
    }

    private async Task<SkillExecutionResult> RunCoreAsync(SkillInvocation invocation, Guid chatId, CancellationToken token)
    {
        var parameters = invocation.Parameters;
        var quote = parameters.GetProperty("quote").GetString()!;
        var path = Text(parameters, "path");
        var diff = Text(parameters, "diff");
        Guid? messageId = Text(parameters, "message_id") is { } id ? Guid.Parse(id) : null;

        var projectId = invocation.ProjectId;
        var skill = await catalog.GetByIdAsync(invocation.SkillId, projectId, token);
        if (skill is not { Enabled: true }) return new("Failed", "The comment suggestion skill is unavailable.", chatId);
        var chat = await chats.GetAsync(projectId, chatId, token);
        if (chat is null) return new("Failed", "Chat not found in this project.", chatId);
        string? role = null, message = null;
        if (messageId is { } expected)
        {
            var found = chat.Messages.FirstOrDefault(item => item.Id == expected);
            if (found is null) return new("Failed", "Message not found in this chat.", chatId);
            role = found.Role == "User" ? "user" : "assistant";
            message = Around(found.Content, quote);
        }

        var global = await settings.LoadAsync(token);
        var project = await projects.GetAsync(projectId, token);
        var connectionId = chat.ConnectionId ?? project?.ConnectionId;
        var connection = global.Connections.FirstOrDefault(item => item.Id == connectionId && item.Enabled)
            ?? global.Connections.FirstOrDefault(item => item.IsDefault && item.Enabled)
            ?? global.Connections.FirstOrDefault(item => item.Enabled);
        if (connection is null) return new("Failed", "No enabled model connection is available.", chatId);

        var conversation = new List<ChatCompletionMessage>
        {
            new("system", SkillMarkdown.Body(skill.Content)),
            new("user", JsonSerializer.Serialize(new
            {
                chat_title = chat.Title,
                fragment = quote.Length <= 2_000 ? quote : quote[..2_000] + "…",
                message_author = role,
                message,
                file = path,
                diff = diff is null ? null : Around(diff, quote)
            }, Prompt))
        };
        var request = new ChatCompletionRequest(connection.BaseUrl, connection.Model,
            await secrets.GetAsync("connection", connection.Id, token),
            "Suggest a review comment", connection.Id, conversation);
        var content = new StringBuilder();
        await foreach (var chunk in completion.StreamAsync(request, token)) content.Append(chunk.Content);
        var text = Clean(content.ToString());
        if (text is null) return new("Skipped", "The model had no comment to suggest.", chatId);
        return new("Completed", "Suggested a review comment.", chatId,
            Output: JsonSerializer.SerializeToElement(new { text }));
    }

    private static string? Text(JsonElement parameters, string name) =>
        parameters.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;

    // A long text is cut to the part around the fragment, which is what the comment is about.
    private static string Around(string content, string quote)
    {
        if (content.Length <= ContextLength) return content;
        var index = Math.Max(0, content.IndexOf(quote, StringComparison.Ordinal));
        var start = Math.Clamp(index + quote.Length / 2 - ContextLength / 2, 0, content.Length - ContextLength);
        return string.Concat(start > 0 ? "…" : "", content.AsSpan(start, ContextLength),
            start + ContextLength < content.Length ? "…" : "");
    }

    private static string? Clean(string content)
    {
        var text = content.Trim().Trim('"', '\'', '`', '«', '»', '“', '”').Trim();
        if (text.Length == 0 || text.Length > MaxLength
            || text.TrimEnd('.').Equals("NONE", StringComparison.OrdinalIgnoreCase)) return null;
        return text;
    }
}
