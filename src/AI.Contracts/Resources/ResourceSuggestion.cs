namespace AI.Contracts.Resources;

/// <summary>
/// A file or directory the composer's "@" list offers. <paramref name="RelativePath"/> is the path
/// under the project directory it was found in, with "/" separators, for display and matching.
/// </summary>
public sealed record ResourceSuggestion(ChatResourceKind Kind, string Path, string RelativePath, PathAccess Access);

/// <summary>
/// The "@" list's files and directories. <paramref name="Complete"/> is false while the Host is
/// still indexing: the list asks again shortly, and better matches may arrive.
/// </summary>
public sealed record ResourceSearchResult(IReadOnlyList<ResourceSuggestion> Items, bool Complete);

/// <summary>
/// A repository with uncommitted changes, offered as "@diff:Name": a project directory inside a
/// work tree, or a work tree found inside one. <paramref name="Location"/> says where it is —
/// the project directory's name and the path below it — so two of the same name can be told apart.
/// </summary>
public sealed record WorkspaceDiffSource(string Path, string Name, int ChangedFiles, string? Location = null);
