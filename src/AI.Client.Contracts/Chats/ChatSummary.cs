// ReSharper disable NotAccessedPositionalProperty.Global

namespace AI.Client.Contracts.Chats;

public sealed record ChatSummary(
    Guid Id,
    Guid ProjectId,
    string Title,
    DateTimeOffset UpdatedAt,
    long Revision,
    DateTimeOffset LastActivityAt,
    bool IsPinned = false,
    DateTimeOffset? PinnedAt = null);
