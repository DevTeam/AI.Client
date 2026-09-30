namespace AI.Contracts.Chats;

public enum ChatArchiveScope { Active, Archived, All }

public sealed record ChatArchiveTarget(Guid ChatId, long Revision);
public sealed record ChatArchivePreviewRequest(DateTimeOffset Before, bool IncludePinned = false);
public sealed record ChatArchivePreview(IReadOnlyList<ChatSummary> Chats, int Skipped, DateTimeOffset Before);
public sealed record ChatArchiveRequest(bool IsArchived, Guid OperationId, IReadOnlyList<ChatArchiveTarget> Targets,
    bool SkipBusy = true);
public sealed record ChatArchiveSkip(Guid ChatId, string Reason);
public sealed record ChatArchiveResult(Guid OperationId, IReadOnlyList<ChatArchiveTarget> Changed,
    IReadOnlyList<ChatArchiveSkip> Skipped);
