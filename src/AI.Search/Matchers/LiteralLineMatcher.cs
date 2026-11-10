namespace AI.Search.Matchers;

/// <summary>
/// A literal search: <see cref="string.IndexOf(ReadOnlySpan{char}, ReadOnlySpan{char}, StringComparison)"/>
/// over the line, which is the vectorized path of the runtime and is what makes a literal search
/// cheaper than a regular expression in ripgrep as well.
/// </summary>
internal sealed class LiteralLineMatcher(string needle, StringComparison comparison, bool wholeWord) : ILineMatcher
{
    private readonly string _needle = needle;
    private readonly StringComparison _comparison = comparison;
    private readonly bool _wholeWord = wholeWord;

    public (int Index, int Length) Find(ReadOnlySpan<char> line)
    {
        var offset = 0;
        while (offset <= line.Length)
        {
            var rest = line[offset..];
            var index = rest.IndexOf(_needle.AsSpan(), _comparison);
            if (index < 0)
            {
                return (-1, 0);
            }

            var absolute = offset + index;
            if (!_wholeWord || WordBoundary.IsWhole(line, absolute, _needle.Length))
            {
                return (absolute, _needle.Length);
            }

            // A rejected candidate is not a match, so the search continues one character further.
            offset = absolute + 1;
        }

        return (-1, 0);
    }
}
