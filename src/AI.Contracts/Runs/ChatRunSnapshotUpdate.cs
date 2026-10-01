namespace AI.Contracts.Runs;

public sealed record ChatRunKey(Guid ChatId, Guid BranchId);

public sealed record ChatRunStreamingAppend(Guid ChatId, Guid BranchId, long Revision, string Content);

/// <summary>
/// More prose of the model step in flight (<see cref="ChatRunSnapshot.DraftContent"/>), published
/// several times a second. The draft does not move the run's revision, so the append names the
/// length it extends instead: a client holding a different draft waits for the next whole snapshot.
/// </summary>
public sealed record ChatRunDraftAppend(Guid ChatId, Guid BranchId, long Revision, int BaseLength, string Content);

/// <param name="KeptWorkspaceChanges">
/// Runs in <paramref name="Runs"/> sent without their <see cref="ChatRunSnapshot.WorkspaceChanges"/>
/// because those are the ones the previous frame carried: the diffs are most of a snapshot, and they
/// change only when a tool finishes.
/// </param>
public sealed record ChatRunSnapshotUpdate(
    bool IsFull,
    IReadOnlyList<ChatRunSnapshot> Runs,
    IReadOnlyList<ChatRunKey> Removed,
    IReadOnlyList<ChatRunStreamingAppend> StreamingAppends,
    IReadOnlyList<ChatRunDraftAppend>? DraftAppends = null,
    IReadOnlyList<ChatRunKey>? KeptWorkspaceChanges = null);
