namespace AI.Search.Matchers;

using AI.Contracts.FileSystem;

/// <summary>Compiles the glob lists of a request once, before the walk starts.</summary>
internal sealed class GlobSetFactory(IPath paths)
{
    private readonly IPath _paths = paths;

    /// <summary>
    /// The set for these patterns. The platform decides whether letter case matters, because a glob
    /// has to select the same files the file system considers to have that name.
    /// </summary>
    public GlobSet Create(IReadOnlyList<string> patterns)
    {
        if (patterns.Count == 0)
        {
            return GlobSet.Empty;
        }

        var ignoreCase = !_paths.IsCaseSensitive;
        var globs = new IPathGlob[patterns.Count];
        for (var index = 0; index < patterns.Count; index++)
        {
            globs[index] = PathGlob.Create(patterns[index], ignoreCase);
        }

        return GlobSet.Create(globs);
    }
}
