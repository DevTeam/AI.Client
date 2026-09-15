namespace AI.Client.Application.Chats;

using AI.Client.Domain.Chats;
using AI.Client.Domain.Projects;

public sealed record StoredChatSummary(
    ChatId Id,
    ProjectId ProjectId,
    string Title,
    DateTimeOffset UpdatedAt,
    long Revision,
    DateTimeOffset LastActivityAt,
    bool IsPinned,
    DateTimeOffset? PinnedAt,
    int BranchCount = 0);
