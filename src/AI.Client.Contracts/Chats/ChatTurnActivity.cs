namespace AI.Client.Contracts.Chats;

public sealed record ChatTurnActivity(
    long Revision,
    Guid TurnId,
    IReadOnlyList<ChatMessageView> Messages);
