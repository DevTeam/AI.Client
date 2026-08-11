namespace AI.Client.Contracts.Chats;

public sealed record ChatDetails(
    Guid Id,
    Guid ProjectId,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision,
    Guid? EndpointProfileId,
    IReadOnlyList<ChatMessageView> Messages,
    IReadOnlyDictionary<Guid, string>? BranchTitles = null);
