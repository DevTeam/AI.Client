namespace AI.Application.Tools.Commands;

/// <summary>The one mask language rules use: <c>*</c> for any run of characters, <c>?</c> for one, no escapes.</summary>
public static class Masks
{
    /// <summary>Whether the text holds a mask character.</summary>
    public static bool IsMask(string text) => text.AsSpan().IndexOfAny('*', '?') >= 0;

    /// <summary>
    /// Whether <paramref name="text"/> matches <paramref name="mask"/> whole. Linear in practice: it
    /// only ever backs up to the last star, so no pattern can make it take forever.
    /// </summary>
    public static bool Matches(string mask, string text, bool ignoreCase)
    {
        int m = 0, t = 0, star = -1, resume = 0;
        while (t < text.Length)
        {
            if (m < mask.Length && (mask[m] == '?' || mask[m] != '*' && Same(mask[m], text[t], ignoreCase)))
            {
                m++;
                t++;
            }
            else if (m < mask.Length && mask[m] == '*')
            {
                star = m++;
                resume = t;
            }
            else if (star >= 0)
            {
                m = star + 1;
                t = ++resume;
            }
            else return false;
        }
        while (m < mask.Length && mask[m] == '*') m++;
        return m == mask.Length;
    }

    private static bool Same(char left, char right, bool ignoreCase) =>
        left == right || ignoreCase && char.ToUpperInvariant(left) == char.ToUpperInvariant(right);
}
