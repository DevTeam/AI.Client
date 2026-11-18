namespace AI.Application.Skills;

using System.Text;
using System.Text.Json;
using AI.Application.Chat;
using AI.Application.Chats;
using AI.Application.Notifications;
using AI.Application.Projects;
using AI.Application.Settings;
using AI.Contracts.Chats;
using Microsoft.Extensions.Logging;

/// <summary>A bounded, tool-using skill run. The model chooses which chat messages to read.</summary>
public sealed class ChatRenameSkill(
    ISkillCatalog catalog,
    IChatService chats,
    IProjectService projects,
    IGlobalSettingsRepository settings,
    IGlobalSecretStore secrets,
    IChatCompletionClient completion,
    IAppDataChangeSignal changes,
    ILogger<ChatRenameSkill> logger) : ISkillExecutor
{
    public string SkillId => "chat-rename";

    // Automatic naming runs as soon as the first message is in the chat; a retry after the first
    // answer also sees that answer.
    private const int AutomaticMessages = 4;

    private static readonly Action<ILogger, Guid, Exception?> SkillFailed =
        LoggerMessage.Define<Guid>(LogLevel.Warning, new EventId(1601, "ChatRenameSkillFailed"),
            "Chat title skill failed for chat {ChatId}");
    // The chat goes to the model as written, not as \u escapes a small model cannot read.
    private static readonly JsonSerializerOptions PromptJson =
        new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly ChatToolDefinition ReadChatTool = new("read_chat",
        "Read user and assistant messages from this chat. Choose the start index and count needed to name it.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                start = new { type = "integer", description = "Zero-based message index; start with 0 for the first turn." },
                count = new { type = "integer", description = "Number of messages to read, from 1 to 12." }
            },
            required = new[] { "start", "count" }
        }));

    public async Task<SkillExecutionResult> RunAsync(SkillInvocation invocation, CancellationToken cancellationToken)
    {
        var target = invocation.Parameters.GetProperty("chat_id").GetString()!;
        var chatId = target == "current"
            ? invocation.CurrentChatId ?? throw new ArgumentException("This skill run has no current chat.")
            : Guid.Parse(target);
        var mode = invocation.Parameters.GetProperty("mode").GetString()!;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            return await RunCoreAsync(invocation, chatId, mode, deadline.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new SkillExecutionResult("Cancelled", "The title skill was cancelled.", chatId);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return new SkillExecutionResult("Failed", "The title skill timed out.", chatId);
        }
        catch (Exception error)
        {
            SkillFailed(logger, chatId, error);
            return new SkillExecutionResult("Failed", error.Message, chatId);
        }
    }

    private async Task<SkillExecutionResult> RunCoreAsync(SkillInvocation invocation, Guid chatId, string mode,
        CancellationToken token)
    {
        var projectId = invocation.ProjectId;
        var skill = await catalog.GetByIdAsync(invocation.SkillId, projectId, token);
        if (skill is not { Enabled: true }) return new("Failed", "The title skill is unavailable.", chatId);
        var chat = await chats.GetAsync(projectId, chatId, token);
        if (chat is null) return new("Failed", "Chat not found in this project.", chatId);
        var automatic = mode == "automatic";
        if (automatic && (!chat.AutoTitlePending || !Readable(chat).Any(message => message.Role == "User")))
            return new("Skipped", "Automatic naming is no longer pending.", chatId);

        var global = await settings.LoadAsync(token);
        var project = await projects.GetAsync(projectId, token);
        var connectionId = chat.ConnectionId ?? project?.ConnectionId;
        // A title is a few words: the connection marked for subtasks, usually a cheaper and faster
        // model, does it as well as the chat's own and does not keep the user waiting for it.
        var connection = global.Connections.FirstOrDefault(item => item.ForSubtasks && item.Enabled)
            ?? global.Connections.FirstOrDefault(item => item.Id == connectionId && item.Enabled)
            ?? global.Connections.FirstOrDefault(item => item.IsDefault && item.Enabled)
            ?? global.Connections.FirstOrDefault(item => item.Enabled);
        if (connection is null) return new("Failed", "No enabled model connection is available.", chatId);
        var apiKey = await secrets.GetAsync("connection", connection.Id, token);
        // Naming needs no deliberation. The effort is lowered only where the connection already
        // sends one, so an endpoint that rejects the parameter never receives it.
        var effort = connection.ReasoningEffort switch
        {
            "none" or "minimal" => connection.ReasoningEffort,
            { Length: > 0 } => "low",
            _ => null
        };

        if (automatic)
        {
            // A new chat holds its first message or two, so they go to the model in the prompt
            // instead of costing a read_chat round trip first.
            var opening = Readable(chat).Take(AutomaticMessages)
                .Select(message => new { message.Role, Content = Short(message.Content) }).ToArray();
            var named = await AnswerAsync(new ChatCompletionRequest(connection.BaseUrl, connection.Model, apiKey,
                "Name this chat", connection.Id,
                [
                    new("system", skill.Content),
                    new("user", $"Run skill {invocation.SkillId} with parameters {invocation.Parameters.GetRawText()}. "
                        + $"The chat so far, oldest first: {JsonSerializer.Serialize(opening, PromptJson)}")
                ], ReasoningEffort: effort), token);
            if (named.Calls.Count > 0) return new("Failed", "The model returned an unexpected tool call.", chatId);
            var suggested = CleanTitle(named.Content);
            if (suggested is null) return new("Failed", "The model returned an invalid title.", chatId);
            var applied = await chats.ApplyAutomaticTitleAsync(projectId, chatId, suggested, token);
            if (applied is null) return new("Skipped", "The chat was renamed while the skill was running.", chatId);
            changes.Notify();
            return new("Completed", $"Renamed chat to '{applied.Title}'.", chatId, applied.Title);
        }

        var conversation = new List<ChatCompletionMessage>
        {
            new("system", skill.Content),
            new("user", $"Run skill {invocation.SkillId} with parameters {invocation.Parameters.GetRawText()}. Call read_chat to inspect its messages first.")
        };
        var request = new ChatCompletionRequest(connection.BaseUrl, connection.Model, apiKey,
            "Name this chat", connection.Id, conversation, [ReadChatTool], ReasoningEffort: effort);
        var first = await AnswerAsync(request, token);
        if (first.Calls is not [{ Name: "read_chat" } call])
            return new("Failed", "The model did not call read_chat.", chatId);
        var (start, count) = ParseRange(call.Arguments);
        var current = await chats.GetAsync(projectId, chatId, token);
        if (current is null) return new("Failed", "Chat was removed during the skill run.", chatId);
        var messages = Readable(current).Skip(start).Take(count)
            .Select(message => new { message.Role, Content = Short(message.Content) }).ToArray();
        conversation.Add(new ChatCompletionMessage("assistant", first.Content, first.Calls));
        conversation.Add(new ChatCompletionMessage("tool", JsonSerializer.Serialize(messages, PromptJson), ToolCallId: call.Id));
        var second = await AnswerAsync(request with { ContextMessages = conversation, Tools = null }, token);
        if (second.Calls.Count > 0) return new("Failed", "The model returned an unexpected tool call.", chatId);
        var title = CleanTitle(second.Content);
        if (title is null) return new("Failed", "The model returned an invalid title.", chatId);
        var updated = await chats.RenameAsync(projectId, chatId, new RenameChatRequest(title, current.Revision), token);
        if (updated is null) return new("Skipped", "The chat changed while the skill was running.", chatId);
        changes.Notify();
        return new("Completed", $"Renamed chat to '{updated.Title}'.", chatId, updated.Title);
    }

    private async Task<(string Content, IReadOnlyList<AI.Contracts.Chat.ChatToolCall> Calls)> AnswerAsync(
        ChatCompletionRequest request, CancellationToken token)
    {
        var content = new StringBuilder();
        var calls = new List<AI.Contracts.Chat.ChatToolCall>();
        await foreach (var chunk in Completion(request, token))
        {
            content.Append(chunk.Content);
            if (chunk.ToolCalls is { } toolCalls) calls.AddRange(toolCalls);
        }
        return (content.ToString(), calls);
    }

    // Kept as a small instance-bound forwarding call so the same configured client and retry policy are used.
    private async IAsyncEnumerable<ChatCompletionChunk> Completion(ChatCompletionRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var chunk in completion.StreamAsync(request, token)) yield return chunk;
    }

    private static (int Start, int Count) ParseRange(string arguments)
    {
        using var document = JsonDocument.Parse(arguments);
        var root = document.RootElement;
        var start = root.GetProperty("start").GetInt32();
        var count = root.GetProperty("count").GetInt32();
        if (start < 0 || start > 1000 || count is < 1 or > 12)
            throw new ArgumentException("The requested chat message range is invalid.");
        return (start, count);
    }

    private static IEnumerable<ChatMessageView> Readable(ChatDetails chat) =>
        chat.Messages.Where(message => message.Role is "User" or "Assistant" && message.ToolCalls is not { Count: > 0 })
            .OrderBy(message => message.CreatedAt);

    private static string Short(string content) => content.Length <= 1500 ? content : content[..1500] + "…";

    private static string? CleanTitle(string content)
    {
        var title = content.Trim().Trim('"', '\'', '`').Trim();
        if (title.Length is < 3 or > 64 || title.Contains('\n') || title.Contains('\r')
            || title.Contains('/') || title.Contains('\\') || title.StartsWith('#')) return null;
        title = title.TrimEnd('.', '!', ' ');
        return title.Length >= 3 ? title : null;
    }
}
