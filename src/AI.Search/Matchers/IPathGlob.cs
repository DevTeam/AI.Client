namespace AI.Search.Matchers;

/// <summary>A glob that decides whether one path is entered or reported.</summary>
internal interface IPathGlob
{
    /// <summary>
    /// Whether the entry matches. A pattern without a separator is matched against its name,
    /// otherwise against its path relative to the search root.
    /// </summary>
    bool IsMatch(string name, string relativePath);
}
