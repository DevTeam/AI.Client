namespace AI.Domain.Chats;

/// <summary>The net workspace changes attached to one completed assistant message.</summary>
public sealed record ChatWorkspaceChangeSet(
    IReadOnlyList<ChatFileChange> Files,
    int Additions,
    int Deletions,
    Guid? UndoId = null);

public sealed record ChatFileChange(
    string Path,
    ChatFileChangeKind Kind,
    int? Additions,
    int? Deletions,
    string? PreviousPath = null,
    string? Diff = null,
    bool IsBinary = false,
    ChatFileChangeConfidence Confidence = ChatFileChangeConfidence.Measured);

public enum ChatFileChangeKind
{
    Added,
    Modified,
    Deleted,
    Renamed,
}

public enum ChatFileChangeConfidence
{
    Measured,
    Approximate,
}
