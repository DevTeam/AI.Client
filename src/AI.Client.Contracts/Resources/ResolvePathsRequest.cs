namespace AI.Client.Contracts.Resources;

/// <summary>Paths as a message wrote them: absolute, or relative to one of the project's directories.</summary>
public sealed record ResolvePathsRequest(IReadOnlyList<string> Paths);

/// <summary>
/// What one written path names. <c>Path</c> and <c>Kind</c> are null when nothing readable by the
/// project is there; otherwise <c>Path</c> is the canonical absolute form a reference would store.
/// </summary>
public sealed record ResolvedPath(string Input, string? Path, ChatResourceKind? Kind);
