namespace AI.Contracts.Resources;

/// <summary>
/// A file or directory the composer's "@" list offers. <paramref name="RelativePath"/> is the path
/// under the project directory it was found in, with "/" separators, for display and matching.
/// </summary>
public sealed record ResourceSuggestion(ChatResourceKind Kind, string Path, string RelativePath, PathAccess Access);

/// <summary>A repository under the project's directories with uncommitted changes, offered as "@diff".</summary>
public sealed record WorkspaceDiffSource(string Path, string Name, int ChangedFiles);
