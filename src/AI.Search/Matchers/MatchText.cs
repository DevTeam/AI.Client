namespace AI.Search.Matchers;

/// <summary>Turns a line and the place of a match in it into the reported text.</summary>
internal static class MatchText
{
    /// <summary>
    /// Characters of a matching line that are reported. A minified file has lines megabytes long,
    /// and the caller only needs the match in its neighbourhood.
    /// </summary>
    public const int MaxCharacters = 400;

    private const string Ellipsis = "…";

    /// <summary>
    /// The line itself when it is short enough, otherwise a window centred on the match, so that a
    /// match far into a long line is still visible.
    /// </summary>
    public static string Window(ReadOnlySpan<char> line, int index, int length)
    {
        if (line.Length <= MaxCharacters)
        {
            return line.ToString();
        }

        var room = Math.Max(MaxCharacters - Math.Min(length, MaxCharacters), 0);
        var start = Math.Clamp(index - (room / 2), 0, Math.Max(line.Length - MaxCharacters, 0));
        var end = Math.Min(start + MaxCharacters, line.Length);
        return string.Concat(start > 0 ? Ellipsis : string.Empty, line[start..end], end < line.Length ? Ellipsis : string.Empty);
    }
}
