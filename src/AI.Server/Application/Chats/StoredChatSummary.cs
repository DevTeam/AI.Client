namespace AI.Application.Chats;

using AI.Domain.Chats;
using AI.Domain.Projects;

public sealed record StoredChatSummary(
    ChatId Id,
    ProjectId ProjectId,
    string Title,
    DateTimeOffset UpdatedAt,
    long Revision,
    DateTimeOffset LastActivityAt,
    bool IsPinned,
    DateTimeOffset? PinnedAt,
    int BranchCount = 0,
    bool HasCurrentManifest = true,
    string? PinOrder = null,
    bool IsEmpty = false,
    DateTimeOffset? ArchivedAt = null,
    Guid? ArchiveOperationId = null, ChatKind Kind = default);
