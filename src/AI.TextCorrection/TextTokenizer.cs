namespace AI.TextCorrection;

public readonly record struct TextRange(int Index, int Length);
public readonly record struct TextToken(int Index, int Length, string Value);

public interface ITextTokenizer
{
    TextRange[] ProtectedRanges(string text);
    TextToken[] Tokenize(string text, string ambiguousSeparators, string changedSeparators, TextRange[] protectedRanges);
}

/// <summary>Linear scanners for the same protected syntax and keyboard-aware boundaries as the composer.</summary>
public sealed class TextTokenizer : ITextTokenizer
{
    public TextRange[] ProtectedRanges(string text)
    {
        var ranges = new List<TextRange>();
        var characters = text.AsSpan();
        for (var index = 0; index < characters.Length;)
        {
            var rest = characters[index..];
            var length = 0;
            if (rest.StartsWith("```", StringComparison.Ordinal) || rest.StartsWith("~~~", StringComparison.Ordinal))
            {
                var end = rest[3..].IndexOf(rest[..3], StringComparison.Ordinal);
                length = end < 0 ? rest.Length : end + 6;
            }
            else if (rest[0] == '`')
            {
                var end = rest[1..].IndexOf('`');
                length = end < 0 ? rest.Length : end + 2;
            }
            else
            {
                var prefix = rest.StartsWith("https://", StringComparison.Ordinal) ? 8
                    : rest.StartsWith("http://", StringComparison.Ordinal) ? 7
                    : (index == 0 || char.IsWhiteSpace(characters[index - 1]))
                        ? rest[0] is '/' or '@' ? 1 : rest.StartsWith("--", StringComparison.Ordinal) ? 2 : 0
                        : 0;
                if (prefix > 0 && rest.Length > prefix && !char.IsWhiteSpace(rest[prefix]))
                {
                    length = prefix + 1;
                    while (length < rest.Length && !char.IsWhiteSpace(rest[length])) length++;
                }
            }
            if (length == 0) index++;
            else
            {
                ranges.Add(new TextRange(index, length));
                index += length;
            }
        }
        return ranges.ToArray();
    }

    public TextToken[] Tokenize(string text, string ambiguousSeparators, string changedSeparators, TextRange[] protectedRanges)
    {
        var tokens = new List<TextToken>();
        var characters = text.AsSpan();
        var rangeIndex = 0;
        for (var index = 0; index < characters.Length;)
        {
            if (char.IsWhiteSpace(characters[index]) || IsSeparator(characters[index], ambiguousSeparators))
            {
                index++;
                continue;
            }
            var start = index++;
            while (index < characters.Length && !char.IsWhiteSpace(characters[index]) && !IsSeparator(characters[index], ambiguousSeparators)) index++;
            // Attach one changed terminal key, such as & -> ?, only before whitespace/end.
            if (index < characters.Length && changedSeparators.Contains(characters[index])
                && (index + 1 == characters.Length || char.IsWhiteSpace(characters[index + 1]))) index++;
            while (rangeIndex < protectedRanges.Length && protectedRanges[rangeIndex].Index + protectedRanges[rangeIndex].Length <= start) rangeIndex++;
            if (rangeIndex < protectedRanges.Length && protectedRanges[rangeIndex].Index < index) continue;
            tokens.Add(new TextToken(start, index - start, text[start..index]));
        }
        return tokens.ToArray();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Tokenizer policies use instance methods.")]
    private bool IsSeparator(char character, string ambiguousSeparators) =>
        (char.IsPunctuation(character) || char.IsSymbol(character)) && !ambiguousSeparators.Contains(character);
}
