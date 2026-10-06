namespace AI.Contracts.Workspace;

public enum WorkspaceUndoFileState { Ready, Undone, Conflict, Unavailable }

public sealed record WorkspaceUndoFileStatus(string Path, WorkspaceUndoFileState State);

public sealed record WorkspaceUndoStatus(Guid UndoId, IReadOnlyList<WorkspaceUndoFileStatus> Files,
    int AppliedCount = 0, string? Error = null);

public sealed record WorkspaceUndoRequest(string? Path = null);
