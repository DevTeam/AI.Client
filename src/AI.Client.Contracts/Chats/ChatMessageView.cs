namespace AI.Client.Contracts.Chats;

public sealed record ChatMessageView(
    Guid Id,
    Guid? ParentId,
    string Role,
    string Content,
    DateTimeOffset CreatedAt,
    bool IsIncomplete = false,
    IReadOnlyList<Chat.ChatToolCall>? ToolCalls = null,
    string? ToolCallId = null);
