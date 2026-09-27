namespace AI.Contracts.Chats;

public sealed record ChatMessageContent(
    long Revision,
    Guid MessageId,
    string Content);
