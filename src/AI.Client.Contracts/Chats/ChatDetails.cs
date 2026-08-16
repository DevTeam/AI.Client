namespace AI.Client.Contracts.Chats;

public sealed record ChatDetails(
    Guid Id,
    Guid ProjectId,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision,
    Guid? ConnectionId,
    IReadOnlyList<ChatMessageView> Messages,
    IReadOnlyList<ChatBranchView>? Branches = null);
