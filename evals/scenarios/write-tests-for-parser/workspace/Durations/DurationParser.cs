namespace Durations;

/// <summary>
/// Parses durations such as "45s", "1h30m" or "2d 4h": whole numbers followed by d, h, m or s,
/// largest unit first, each unit at most once, optional spaces between parts.
/// </summary>
public static class DurationParser
{
    private static readonly (char Unit, TimeSpan Size)[] Units =
        [('d', TimeSpan.FromDays(1)), ('h', TimeSpan.FromHours(1)), ('m', TimeSpan.FromMinutes(1)), ('s', TimeSpan.FromSeconds(1))];

    public static TimeSpan Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var total = TimeSpan.Zero;
        var next = 0;
        var index = 0;
        var any = false;
        while (index < text.Length)
        {
            if (text[index] == ' ') { index++; continue; }
            var start = index;
            while (index < text.Length && char.IsAsciiDigit(text[index])) index++;
            if (index == start || index == text.Length) throw new FormatException($"Expected a number and a unit at {start}.");
            var unit = Array.FindIndex(Units, item => item.Unit == text[index]);
            if (unit < next) throw new FormatException($"Unknown or repeated unit '{text[index]}'.");
            total += Units[unit].Size * long.Parse(text[start..index], System.Globalization.CultureInfo.InvariantCulture);
            next = unit + 1;
            index++;
            any = true;
        }

        return any ? total : throw new FormatException("The duration is empty.");
    }
}
