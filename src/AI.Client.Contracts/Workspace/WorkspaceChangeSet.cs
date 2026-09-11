namespace AI.Client.Contracts.Workspace;

/// <summary>
/// What a run changed on disk, as a net result rather than a log of edits: a file touched five
/// times appears once, with the difference between how it started and how it stands now.
/// </summary>
/// <param name="IsComplete">
/// False when something ran that the Host could not fully observe — an external process, or a
/// third-party tool that may write files without reporting what it wrote. The counts below are
/// then a floor, not a total, and the UI must say so rather than implying the list is exhaustive.
/// </param>
public sealed record WorkspaceChangeSet(
    IReadOnlyList<FileChange> Files,
    int Additions,
    int Deletions,
    bool IsComplete)
{
    public static WorkspaceChangeSet Empty { get; } = new([], 0, 0, true);

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
