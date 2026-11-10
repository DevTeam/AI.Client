namespace AI.Search.Matchers;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// One glob compiled to a regular expression: <c>*</c> matches within a path segment, <c>**</c>
/// crosses segments, <c>?</c> matches one character, <c>[abc]</c> and <c>[!abc]</c> are character
/// classes, and a backslash escapes the next character. A pattern with no separator is matched
/// against an entry's name, so <c>*.cs</c> finds a file at any depth.
/// </summary>
internal sealed class PathGlob : IPathGlob
{
    private readonly Regex _regex;
    private readonly bool _fullPath;

    private PathGlob(Regex regex, bool fullPath)
    {
        _regex = regex;
        _fullPath = fullPath;
    }

    /// <summary>Compiles the pattern; <paramref name="ignoreCase"/> follows the platform's path comparison.</summary>
    /// <exception cref="ArgumentException">The pattern names nothing.</exception>
    public static PathGlob Create(string pattern, bool ignoreCase)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new ArgumentException("A glob cannot be empty.", nameof(pattern));
        }

        var normalized = pattern.Replace('\\', '/');
        // A leading "./" or "/" ties the pattern to the root and adds nothing to its meaning."
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        if (normalized.StartsWith('/'))
        {
            normalized = normalized[1..];
        }

        var fullPath = normalized.Contains('/', StringComparison.Ordinal);
        var options = RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        return new PathGlob(new Regex(Translate(normalized, fullPath), options, TimeSpan.FromSeconds(1)), fullPath);
    }

    public bool IsMatch(string name, string relativePath) =>
        _regex.IsMatch(_fullPath ? Normalize(relativePath) : name);

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string Translate(string pattern, bool fullPath)
    {
        var builder = new StringBuilder("^");
        if (!fullPath && !pattern.StartsWith("**", StringComparison.Ordinal))
        {
            // A bare name is matched at any depth, the way ".gitignore" and "*.cs" are read.
            builder.Append("(?:.*/)?");
        }

        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            switch (character)
            {
                case '*' when index + 1 < pattern.Length && pattern[index + 1] == '*':
                    // "**/" also matches no directory at all, so "src/**/*.cs" finds "src/a.cs".
                    builder.Append(".*");
                    index++;
                    if (index + 1 < pattern.Length && pattern[index + 1] == '/')
                    {
                        index++;
                    }

                    break;
                case '*':
                    builder.Append("[^/]*");
                    break;
                case '?':
                    builder.Append("[^/]");
                    break;
                case '[' when pattern.IndexOf(']', index + 1) is var close && close > index + 1:
                    var set = pattern[(index + 1)..close];
                    builder.Append('[').Append(set.StartsWith('!') ? "^" + Regex.Escape(set[1..]) : Regex.Escape(set)).Append(']');
                    index = close;
                    break;
                case '\\' when index + 1 < pattern.Length:
                    builder.Append(Regex.Escape(pattern[++index].ToString()));
                    break;
                default:
                    builder.Append(Regex.Escape(character.ToString()));
                    break;
            }
        }

        return builder.Append('$').ToString();
    }
}
