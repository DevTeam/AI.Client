namespace AI.Search.Matchers;

using System.Text.RegularExpressions;

/// <summary>
/// A regular expression search over the line. Matches are enumerated over the span instead of being
/// materialized as <see cref="Match"/> objects, so a scan of many lines does not allocate a match
/// per line.
/// </summary>
internal sealed class RegexLineMatcher(Regex regex, bool wholeWord, byte[]? requiredBytes = null) : ILineMatcher
{
    private readonly Regex _regex = regex;
    private readonly bool _wholeWord = wholeWord;
    private readonly byte[]? _requiredBytes = requiredBytes;

    /// <inheritdoc/>
    public byte[]? RequiredBytes => _requiredBytes;

    public (int Index, int Length) Find(ReadOnlySpan<char> line)
    {
        foreach (var match in _regex.EnumerateMatches(line))
        {
            if (!_wholeWord || WordBoundary.IsWhole(line, match.Index, match.Length))
            {
                return (match.Index, match.Length);
            }
        }

        return (-1, 0);
    }
}
