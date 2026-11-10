namespace AI.Search.Matchers;

using System.Text.RegularExpressions;

/// <summary>
/// A set of globs: a file is searched when it matches one of the include globs, and is left out —
/// with its whole subtree, for a directory — when it matches an exclude glob. An empty set matches
/// nothing, which is what makes an empty include list mean "every file" without a special case in
/// the walk.
/// </summary>
internal sealed class GlobSet
{
    /// <summary>A set no path matches.</summary>
    public static readonly GlobSet Empty = new([]);

    private readonly IPathGlob[] _globs;

    private GlobSet(IPathGlob[] globs) => _globs = globs;

    public bool IsEmpty => _globs.Length == 0;

    public static GlobSet Create(IEnumerable<IPathGlob> globs) => new(globs.ToArray());

    /// <summary>
    /// Whether any glob matches. Written as a loop over an array: this runs once per entry of every
    /// directory the walk enters, which is the hottest filter in a search.
    /// </summary>
    public bool IsMatch(string name, string relativePath)
    {
        var globs = _globs;
        for (var index = 0; index < globs.Length; index++)
        {
            if (globs[index].IsMatch(name, relativePath))
            {
                return true;
            }
        }

        return false;
    }
}
