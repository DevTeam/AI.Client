namespace AI.Client.Mcp.BuiltIn.Files;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Minimal glob support for search patterns: '*' matches within a path segment, '**' crosses segments and '?' matches one character.
/// A pattern without a separator is matched against the entry name, otherwise against the path relative to the search root.
/// </summary>
internal sealed partial class GlobPattern
{
    private readonly Regex _regex;

    private GlobPattern(Regex regex, bool matchesFullPath)
    {
        _regex = regex;
        MatchesFullPath = matchesFullPath;
    }

    public bool MatchesFullPath { get; }

    public static GlobPattern Parse(string pattern)
    {
        var full = pattern.Contains('/', StringComparison.Ordinal) || pattern.Contains('\\', StringComparison.Ordinal);
        var builder = new StringBuilder("^");
        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            switch (character)
            {
                case '*' when index + 1 < pattern.Length && pattern[index + 1] == '*':
                    builder.Append(".*");
                    index++;
                    break;
                case '*':
                    builder.Append("[^/]*");
                    break;
                case '?':
                    builder.Append("[^/]");
                    break;
                case '/':
                case '\\':
                    builder.Append('/');
                    break;
                default:
                    builder.Append(Regex.Escape(character.ToString()));
                    break;
            }
        }

        builder.Append('$');
        return new GlobPattern(
            new Regex(builder.ToString(), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)),
            full);
    }

    public bool IsMatch(string name, string relativePath) =>
        _regex.IsMatch(Normalize(MatchesFullPath ? relativePath : name));

    private static string Normalize(string value) => value.Replace('\\', '/');
}
