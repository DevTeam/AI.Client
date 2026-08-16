namespace AI.Client.Domain.Chats;

using Common;

public sealed class ChatMessage(
    ChatMessageId id,
    ChatMessageId? parentId,
    ChatMessageRole role,
    string content,
    DateTimeOffset createdAt,
    bool isIncomplete = false)
{
    public ChatMessageId Id { get; } = id;

    public ChatMessageId? ParentId { get; } = parentId;

    public ChatMessageRole Role { get; } = role;

    public string Content { get; } = string.IsNullOrWhiteSpace(content)
        ? throw new DomainException("Chat message content cannot be empty.")
        : content.Trim();

    public DateTimeOffset CreatedAt { get; } = createdAt;

    public bool IsIncomplete { get; } = isIncomplete;
}
