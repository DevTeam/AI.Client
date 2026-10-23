// ReSharper disable NotAccessedPositionalProperty.Global

namespace AI.Contracts.Chats;

public sealed record ChatSummary(
    Guid Id,
    Guid ProjectId,
    string Title,
    DateTimeOffset UpdatedAt,
    long Revision,
    DateTimeOffset LastActivityAt,
    bool IsPinned = false,
    DateTimeOffset? PinnedAt = null,
    // Alternative branches only: the main branch is the chat itself and is never counted.
    int BranchCount = 0,
    // No message has been written yet: the sidebar hides such a chat until its first message.
    bool IsEmpty = false,
    DateTimeOffset? ArchivedAt = null,
    Guid? ArchiveOperationId = null,
    string Kind = "conversation",
    // A schedule may belong to any branch and is stored outside the chat document, so Kind alone
    // cannot tell the sidebar which chats are scheduled.
    bool HasSchedule = false);
