namespace AI.Search.Matchers;

using System.Text;

/// <summary>
/// The text every match of a regular expression must contain. A line that does not hold it cannot
/// match, so the expression is never called for that line — which is what ripgrep calls a prefilter,
/// and what makes a regex search cost about what the same literal search costs.
/// </summary>
/// <remarks>
/// An expression is a compiled automaton and starts paying for every line it is handed, so calling it
/// per line dominates a search of many short lines: on a corpus of twenty thousand generated files
/// one call per line cost more than four times what the expression itself costs on the lines that
/// pass the prefilter. The extraction below is deliberately conservative and always sound: it takes
/// the longest run of literal characters that every match is known to contain, and gives up
/// entirely — returning null, so nothing is skipped — when the pattern is case-insensitive, when it
/// has a group or an alternation, or when the run it found is shorter than
/// <see cref="MinLength"/>.
/// </remarks>
internal static class RequiredLiteral
{
    /// <summary>
    /// Shortest literal worth looking for. One character would match on nearly every line of a
    /// natural text, and paying for the scan only to call the expression anyway is slower than
    /// calling it directly.
    /// </summary>
    public const int MinLength = 2;

    /// <summary>Characters that make a required run impossible to promise.</summary>
    private static readonly char[] NonLiteralStructure = ['(', ')', '|'];

    /// <summary>
    /// The UTF-8 bytes of the required literal, or null when the pattern promises none. The bytes are
    /// ASCII only, which is what lets them be searched in the file before it is decoded.
    /// </summary>
    /// <param name="pattern">The regular expression as the caller wrote it.</param>
    /// <param name="ignoreCase">Whether the expression matches letter case loosely.</param>
    public static byte[]? Find(string pattern, bool ignoreCase)
    {
        if (ignoreCase || pattern.IndexOfAny(NonLiteralStructure) >= 0)
        {
            return null;
        }

        var best = string.Empty;
        var run = new StringBuilder();
        var index = 0;
        while (index < pattern.Length)
        {
            switch (pattern[index])
            {
                case '\\':
                    index++;
                    if (index == pattern.Length)
                    {
                        // A trailing backslash is not a usable pattern; promise nothing.
                        return null;
                    }

                    // An escape of a letter or a digit names a class — \d, \s, \w, \b — and a class
                    // is not a literal; an escape of punctuation names the character itself.
                    if (char.IsAsciiLetterOrDigit(pattern[index]))
                    {
                        Break();
                    }
                    else
                    {
                        run.Append(pattern[index]);
                    }

                    break;

                case '.':
                case '^':
                case '$':
                    Break();
                    break;

                case '[':
                    Break();
                    index = SkipClass(pattern, index);
                    break;

                // A quantifier makes the element before it optional for `*` and `?`, and repeats it for
                // `+`. In the last case the character is still required, but telling the three apart
                // buys one character and risks being wrong, so the run gives it up.
                case '*':
                case '+':
                case '?':
                    Break(dropLast: true);
                    break;

                case '{':
                    Break(dropLast: true);
                    while (index < pattern.Length && pattern[index] != '}')
                    {
                        index++;
                    }

                    break;

                default:
                    if (pattern[index] is >= ' ' and <= '~')
                    {
                        run.Append(pattern[index]);
                    }
                    else
                    {
                        // A non-ASCII character: its bytes are a UTF-8 encoding rather than the
                        // character, which is not something to promise about a file's bytes.
                        Break();
                    }

                    break;
            }

            index++;
        }

        Break();
        return best.Length >= MinLength ? Encoding.UTF8.GetBytes(best) : null;

        void Break(bool dropLast = false)
        {
            if (dropLast && run.Length > 0)
            {
                run.Length--;
            }

            if (run.Length > best.Length)
            {
                best = run.ToString();
            }

            run.Clear();
        }
    }

    /// <summary>The index of the <c>]</c> that closes the class opened at <paramref name="open"/>.</summary>
    private static int SkipClass(string pattern, int open)
    {
        var index = open + 1;
        if (index < pattern.Length && pattern[index] == '^')
        {
            index++;
        }

        // A `]` straight after the opening bracket is the character itself, not the end.
        if (index < pattern.Length && pattern[index] == ']')
        {
            index++;
        }

        while (index < pattern.Length && pattern[index] != ']')
        {
            if (pattern[index] == '\\')
            {
                index++;
            }

            index++;
        }

        return Math.Min(index, pattern.Length - 1);
    }
}
