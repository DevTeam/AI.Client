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
/// One model call without tools: the last user message and the answer to it go in, a draft of the
/// user's reply comes out. The chat is only read; the draft is handed back as the run's output.
/// </summary>
public sealed class ChatReplySuggestSkill(
    ISkillCatalog catalog,
    IChatService chats,
    IProjectService projects,
    IGlobalSettingsRepository settings,
    IGlobalSecretStore secrets,
    IChatCompletionClient completion,
    ILogger<ChatReplySuggestSkill> logger) : ISkillExecutor
{
    public string SkillId => "chat-reply-suggest";

    private const int MaxLength = 300;

    private static readonly Action<ILogger, Guid, Exception?> SkillFailed =
        LoggerMessage.Define<Guid>(LogLevel.Warning, new EventId(1602, "ChatReplySuggestSkillFailed"),
            "Reply suggestion skill failed for chat {ChatId}");

    public async Task<SkillExecutionResult> RunAsync(SkillInvocation invocation, CancellationToken cancellationToken)
    {
        var parameters = invocation.Parameters;
        var target = parameters.GetProperty("chat_id").GetString()!;
        var chatId = target == "current"
            ? invocation.CurrentChatId ?? throw new ArgumentException("This skill run has no current chat.")
            : Guid.Parse(target);
        Guid? branchId = parameters.TryGetProperty("branch_id", out var branch) ? Guid.Parse(branch.GetString()!)
            : target == "current" ? invocation.CurrentBranchId : null;
        Guid? messageId = parameters.TryGetProperty("message_id", out var message) ? Guid.Parse(message.GetString()!) : null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            return await RunCoreAsync(invocation, chatId, branchId ?? chatId, messageId, deadline.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new SkillExecutionResult("Cancelled", "The reply suggestion was cancelled.", chatId);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return new SkillExecutionResult("Failed", "The reply suggestion timed out.", chatId);
        }
        catch (Exception error)
        {
            SkillFailed(logger, chatId, error);
            return new SkillExecutionResult("Failed", error.Message, chatId);
        }
    }

    private async Task<SkillExecutionResult> RunCoreAsync(SkillInvocation invocation, Guid chatId, Guid branchId,
        Guid? messageId, CancellationToken token)
    {
        var projectId = invocation.ProjectId;
        var skill = await catalog.GetByIdAsync(invocation.SkillId, projectId, token);
        if (skill is not { Enabled: true }) return new("Failed", "The reply suggestion skill is unavailable.", chatId);
        var chat = await chats.GetAsync(projectId, chatId, token);
        if (chat is null) return new("Failed", "Chat not found in this project.", chatId);
        var head = chat.Branches?.SingleOrDefault(item => item.Id == branchId)?.HeadMessageId;
        if (head is null) return new("Failed", "Branch not found in this chat.", chatId);
        if (messageId is { } expected && expected != head)
            return new("Skipped", "The branch has moved past that answer.", chatId);
        var byId = chat.Messages.ToDictionary(item => item.Id);
        if (!byId.TryGetValue(head.Value, out var answer) || answer.Role != "Assistant" || answer.IsIncomplete
            || string.IsNullOrWhiteSpace(answer.Content))
            return new("Skipped", "The branch does not end with a finished answer.", chatId);
        var question = Ancestors(byId, answer).FirstOrDefault(item => item.Role == "User");

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
                user_message = question is null ? null : Tail(question.Content, 2_000),
                assistant_answer = Tail(answer.Content, 6_000)
            }))
        };
        var request = new ChatCompletionRequest(connection.BaseUrl, connection.Model,
            await secrets.GetAsync("connection", connection.Id, token),
            "Suggest a reply", connection.Id, conversation);
        var content = new StringBuilder();
        await foreach (var chunk in completion.StreamAsync(request, token)) content.Append(chunk.Content);
        var text = Clean(content.ToString());
        if (text is null) return new("Skipped", "The model had no reply to suggest.", chatId);
        return new("Completed", "Suggested a reply.", chatId,
            Output: JsonSerializer.SerializeToElement(new { text, message_id = head.Value }));
    }

    private static IEnumerable<ChatMessageView> Ancestors(Dictionary<Guid, ChatMessageView> byId, ChatMessageView message)
    {
        for (var current = message; current.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent); current = parent)
            yield return parent;
    }

    // The end of a long answer is where its question or proposal is, so that is the part kept.
    private static string Tail(string content, int length) => content.Length <= length ? content : "…" + content[^length..];

    private static string? Clean(string content)
    {
        var text = content.Trim().Trim('"', '\'', '`', '«', '»', '“', '”').Trim();
        if (text.Length == 0 || text.Length > MaxLength
            || text.TrimEnd('.').Equals("NONE", StringComparison.OrdinalIgnoreCase)) return null;
        return text;
    }
}
