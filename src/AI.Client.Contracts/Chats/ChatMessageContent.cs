namespace AI.Client.Contracts.Chats;

public sealed record ChatMessageContent(
    long Revision,
    Guid MessageId,
    string Content);
