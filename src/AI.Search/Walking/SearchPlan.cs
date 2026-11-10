namespace AI.Search.Walking;

using AI.Search.Matchers;

/// <summary>One search, resolved and compiled: what to look for, where, and within which limits.</summary>
internal sealed record SearchPlan(
    string Root,
    bool IsFile,
    GlobSet Include,
    GlobSet Exclude,
    bool RespectIgnoreFiles,
    bool IncludeHidden,
    bool SkipBinary,
    long MaxFileBytes,
    int MaxMatches,
    int MaxMatchesPerFile,
    MatchOutput Output,
    int Parallelism)
{
    /// <summary>
    /// Whether a file is worth scanning: it has to match an include glob — when any were given —
    /// and must not match an exclude glob.
    /// </summary>
    public bool Accepts(string name, string relativePath) =>
        (Include.IsEmpty || Include.IsMatch(name, relativePath)) && !Exclude.IsMatch(name, relativePath);
}
