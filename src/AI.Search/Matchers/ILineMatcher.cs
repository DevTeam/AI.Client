namespace AI.Search.Matchers;

/// <summary>Finds the first acceptable match of a pattern in one line of text.</summary>
internal interface ILineMatcher
{
    /// <summary>
    /// Bytes every matching line must contain, or null when the pattern promises none. The scanner
    /// looks for them before it decodes a line, so a line that cannot match costs a byte comparison
    /// instead of a call into a compiled expression.
    /// </summary>
    byte[]? RequiredBytes => null;

    /// <summary>
    /// The 0-based character index and the length of the first match, or a negative index when the
    /// line holds none. A matcher that requires whole words skips a match that touches a letter.
    /// </summary>
    (int Index, int Length) Find(ReadOnlySpan<char> line);
}

/// <summary>Word boundaries as ripgrep's <c>-w</c> means them.</summary>
internal static class WordBoundary
{
    /// <summary>
    /// Whether the match at the index is a whole word: no letter, digit or underscore directly
    /// before or after it. Unicode-aware, because a word is not only ASCII letters.
    /// </summary>
    public static bool IsWhole(ReadOnlySpan<char> line, int index, int length)
    {
        var before = index == 0 || !IsWordCharacter(line[index - 1]);
        var end = index + length;
        var after = end >= line.Length || !IsWordCharacter(line[end]);
        return before && after;
    }

    private static bool IsWordCharacter(char character) =>
        char.IsLetterOrDigit(character) || character == '_';
}
