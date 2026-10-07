namespace AI.Domain.Chats;

using Common;

public sealed class ChatMessage(
    ChatMessageId id,
    ChatMessageId? parentId,
    ChatMessageRole role,
    string content,
    DateTimeOffset createdAt,
    bool isIncomplete = false,
    IReadOnlyList<ChatToolCall>? toolCalls = null,
    string? toolCallId = null,
    ChatWorkspaceChangeSet? workspaceChanges = null,
    IReadOnlyList<AI.Domain.Resources.ChatResource>? resources = null,
    bool allowEmptyAfterResourceRemoval = false,
    ChatMessageDelivery delivery = ChatMessageDelivery.Turn,
    ChatMessageSender? sender = null)
{
    public ChatMessageId Id { get; } = id;

    public ChatMessageId? ParentId { get; } = parentId;

    public ChatMessageRole Role { get; } = role;

    public string Content { get; } = !allowEmptyAfterResourceRemoval && string.IsNullOrWhiteSpace(content)
        && toolCalls is not { Count: > 0 } && resources is not { Count: > 0 }
        ? throw new DomainException("Chat message content cannot be empty.")
        : content.Trim();

    public DateTimeOffset CreatedAt { get; } = createdAt;

    public bool IsIncomplete { get; } = isIncomplete;

    public IReadOnlyList<ChatToolCall>? ToolCalls { get; } = toolCalls?.ToArray();
    public string? ToolCallId { get; } = toolCallId;
    public IReadOnlyList<AI.Domain.Resources.ChatResource>? Resources { get; } = resources?.ToArray();
    public ChatWorkspaceChangeSet? WorkspaceChanges { get; } = workspaceChanges is null
        ? null
        : workspaceChanges with { Files = workspaceChanges.Files.ToArray() };
    public ChatMessageDelivery Delivery { get; } = delivery;
    public ChatMessageSender? Sender { get; } = sender;

    /// <summary>Whether this message opens a turn, or stands as one as an aside: any user message not taken by a running turn.</summary>
    public bool StartsTurn => Role == ChatMessageRole.User && Delivery != ChatMessageDelivery.InTurn;
}
