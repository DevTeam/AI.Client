namespace AI.Client.Contracts.Resources;

/// <summary>Paths as a message wrote them: absolute, or relative to one of the project's directories.</summary>
public sealed record ResolvePathsRequest(IReadOnlyList<string> Paths);

/// <summary>
/// What one written path names. <c>Path</c> and <c>Kind</c> are null when nothing is there;
/// otherwise <c>Path</c> is the canonical absolute form a reference would store. <c>Access</c> is
/// what the project's tools may do there: <c>None</c> for an absolute path outside every directory
/// the project may read — the person can then grant access to it; a relative path is only ever
/// found inside those directories.
/// </summary>
public sealed record ResolvedPath(string Input, string? Path, ChatResourceKind? Kind, PathAccess Access = PathAccess.None);

/// <summary>What a project's directory grants allow at a path.</summary>
public enum PathAccess { None, Read, ReadWrite }
