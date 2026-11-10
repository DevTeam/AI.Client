namespace AI.Search.Ignoring;

using System.Text;
using System.Text.RegularExpressions;
using AI.Contracts.FileSystem;
using Matchers;

/// <summary>One parsed ignore file line: a glob, in which direction it decides, and where it applies.</summary>
internal sealed record IgnoreGlob(IPathGlob Glob, bool Negated, bool DirectoryOnly, string? BaseDirectory);

/// <summary>
/// The ignore rules in force at one point of the walk. ripgrep reads the files a repository or a
/// directory has — <c>.gitignore</c> from the repository root down, <c>.ignore</c> at any level, and
/// <c>.rgignore</c>, which wins over both — and matches entries as Git does: the last matching rule
/// decides, <c>!</c> re-includes, a trailing <c>/</c> applies to directories only, and a pattern
/// with a slash is anchored to the directory of its file.
/// </summary>
/// <remarks>
/// A rule list is built once per ignore file and shared by every directory below it, so a walk with
/// ten thousand files parses a handful of files instead of re-reading the rules of every directory
/// it enters. The global excludes file is not read: that is the machine's setting rather than the
/// repository's, and a search that silently applies somebody else's ignore list is a search that
/// silently finds too little. The repository's own <c>info/exclude</c> is read, before every
/// <c>.gitignore</c>, because it belongs to the clone whose files are being searched.
/// </remarks>
internal sealed class IgnoreRules
{
    /// <summary>Ignore files in the order they are applied, the last of them winning.</summary>
    public static readonly string[] FileNames = [".gitignore", ".ignore", ".rgignore"];

    /// <summary>The rules that decide nothing: what a walk outside any ignore file starts with.</summary>
    public static readonly IgnoreRules None = new([], StringComparison.Ordinal);

    private readonly IgnoreGlob[] _globs;
    private readonly StringComparison _comparison;

    private IgnoreRules(IgnoreGlob[] globs, StringComparison comparison)
    {
        _globs = globs;
        _comparison = comparison;
    }

    public bool IsEmpty => _globs.Length == 0;

    /// <summary>These rules with <paramref name="more"/> applied after them, so that the stricter of the two wins.</summary>
    public IgnoreRules And(IgnoreRules more) =>
        more.IsEmpty ? this : IsEmpty ? more : new IgnoreRules([.. _globs, .. more._globs], _comparison);

    /// <summary>
    /// The rules one ignore file states. <paramref name="baseDirectory"/> is the directory the file
    /// belongs to, which anchors its patterns with a slash.
    /// </summary>
    public static IgnoreRules Parse(IEnumerable<string> lines, string baseDirectory, StringComparison comparison)
    {
        var globs = new List<IgnoreGlob>();
        foreach (var line in lines)
        {
            if (Parse(line, baseDirectory, comparison) is { } glob)
            {
                globs.Add(glob);
            }
        }

        return globs.Count == 0 ? None : new IgnoreRules(globs.ToArray(), comparison);
    }

    /// <summary>
    /// Whether the entry is ignored. <paramref name="relativePath"/> is relative to the search root
    /// and anchored rules are matched against the path relative to their own directory.
    /// </summary>
    public bool IsIgnored(string root, string path, bool isDirectory, IPath paths)
    {
        if (_globs.Length == 0)
        {
            return false;
        }

        var verdict = false;
        var relative = paths.GetRelativePath(root, path).Replace('\\', '/');
        foreach (var glob in _globs)
        {
            if (glob.DirectoryOnly && !isDirectory)
            {
                continue;
            }

            var candidate = glob.BaseDirectory is null ? relative : paths.GetRelativePath(glob.BaseDirectory, path).Replace('\\', '/');
            if (glob.Glob.IsMatch(paths.GetFileName(path), candidate))
            {
                verdict = !glob.Negated;
            }
        }

        return verdict;
    }

    private static IgnoreGlob? Parse(string rawLine, string baseDirectory, StringComparison comparison)
    {
        var line = TrimTrailingSpaces(rawLine);
        if (line.Length == 0 || line[0] == '#')
        {
            return null;
        }

        var negated = line[0] == '!';
        if (negated)
        {
            line = line[1..];
        }
        else if (line.StartsWith("\\!", StringComparison.Ordinal) || line.StartsWith("\\#", StringComparison.Ordinal))
        {
            line = line[1..];
        }

        var directoryOnly = line.EndsWith('/');
        if (directoryOnly)
        {
            line = line.TrimEnd('/');
        }

        if (line.Length == 0)
        {
            return null;
        }

        // A slash anywhere but the end ties the pattern to its own directory; without one it
        // matches a name at any depth below it, which is why the anchor is kept separately.
        var anchored = line.Contains('/', StringComparison.Ordinal);
        line = line.TrimStart('/');
        if (line.Length == 0)
        {
            return null;
        }

        try
        {
            var glob = PathGlob.Create(line, comparison == StringComparison.OrdinalIgnoreCase, anchored);
            return new IgnoreGlob(glob, negated, directoryOnly, anchored ? baseDirectory : null);
        }
        catch (ArgumentException)
        {
            // Git skips a pattern it cannot read, and so does this.
            return null;
        }
    }

    private static string TrimTrailingSpaces(string line)
    {
        var end = line.Length;
        while (end > 0 && line[end - 1] is ' ' or '\t' or '\r' && !(end > 1 && line[end - 2] == '\\'))
        {
            end--;
        }

        return line[..end];
    }
}
