namespace AI.Contracts.Workspace;

/// <summary>
/// What a run changed on disk, as a net result rather than a log of edits: a file touched five
/// times appears once, with the difference between how it started and how it stands now.
/// </summary>
public sealed record WorkspaceChangeSet(
    IReadOnlyList<FileChange> Files,
    int Additions,
    int Deletions,
    Guid? UndoId = null)
{
    public static WorkspaceChangeSet Empty { get; } = new([], 0, 0);

    public bool IsEmpty => Files.Count == 0;
}

/// <param name="Additions">Lines added, net of the whole run. Null for a binary file, where lines mean nothing.</param>
/// <param name="Confidence">
/// Whether the numbers were measured against a baseline this run captured, or inferred.
/// </param>
public sealed record FileChange(
    string Path,
    FileChangeKind Kind,
    int? Additions,
    int? Deletions,
    string? PreviousPath = null,
    string? Diff = null,
    bool IsBinary = false,
    FileChangeConfidence Confidence = FileChangeConfidence.Measured);

public enum FileChangeKind
{
    Added,
    Modified,
    Deleted,
    Renamed,
}

public enum FileChangeConfidence
{
    /// <summary>Compared against content captured before the run first touched this path.</summary>
    Measured,

    /// <summary>
    /// The file changed, but the exact line counts could not be established — it was already gone,
    /// unreadable, or too large to diff within the cap.
    /// </summary>
    Approximate,
}
