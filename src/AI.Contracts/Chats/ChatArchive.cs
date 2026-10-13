namespace AI.Contracts.Chats;

public enum ChatArchiveScope { Active, Archived, All }

public sealed record ChatArchiveTarget(Guid ChatId, long Revision);
public sealed record ChatArchivePreviewRequest(DateTimeOffset Before, bool IncludePinned = false,
    bool IncludeScheduled = false);
public sealed record ChatArchivePreview(IReadOnlyList<ChatSummary> Chats, int Skipped, DateTimeOffset Before);
public sealed record ChatArchiveRequest(bool IsArchived, Guid OperationId, IReadOnlyList<ChatArchiveTarget> Targets,
    bool SkipBusy = true, bool RemoveSchedules = false);
public sealed record ChatArchiveSkip(Guid ChatId, string Reason);
public sealed record ChatArchiveResult(Guid OperationId, IReadOnlyList<ChatArchiveTarget> Changed,
    IReadOnlyList<ChatArchiveSkip> Skipped);
public sealed record ChatArchiveDeleteRequest(IReadOnlyList<ChatArchiveTarget> Targets);
