namespace AI.Contracts.Resources;

/// <summary>Paths as a message wrote them: absolute, or relative to one of the project's directories.</summary>
public sealed record ResolvePathsRequest(IReadOnlyList<string> Paths);

/// <summary>
/// What one written path names. <c>Path</c> and <c>Kind</c> are null when nothing is there;
/// otherwise <c>Path</c> is the canonical absolute form a reference would store. <c>Access</c> is
/// what the project's tools may do there: <c>None</c> for an absolute path outside every directory
/// the project may read — the person can then grant access to it; a relative path is only ever
/// found inside those directories. <c>Temporary</c> is true for a path in the project's chat scratch
/// space, which is deleted with its chat.
/// </summary>
public sealed record ResolvedPath(string Input, string? Path, ChatResourceKind? Kind, PathAccess Access = PathAccess.None,
    bool Temporary = false);

/// <summary>The private scratch directory of one chat, shared by its branches and deleted with it.</summary>
public sealed record ChatTemporaryDirectoryInfo(string Path);

/// <summary>What a project's directory grants allow at a path.</summary>
public enum PathAccess { None, Read, ReadWrite }
