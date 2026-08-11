namespace AI.Client.Contracts.Chats;

public sealed record ChatSummary(Guid Id, Guid ProjectId, string Title, DateTimeOffset UpdatedAt, long Revision);
