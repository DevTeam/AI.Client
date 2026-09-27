namespace AI.Contracts.Runs;

public sealed record ChatRunKey(Guid ChatId, Guid BranchId);

public sealed record ChatRunStreamingAppend(Guid ChatId, Guid BranchId, long Revision, string Content);

public sealed record ChatRunSnapshotUpdate(
    bool IsFull,
    IReadOnlyList<ChatRunSnapshot> Runs,
    IReadOnlyList<ChatRunKey> Removed,
    IReadOnlyList<ChatRunStreamingAppend> StreamingAppends);
