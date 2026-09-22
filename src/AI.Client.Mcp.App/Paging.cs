namespace AI.Client.Mcp.App;

using System.Globalization;
using System.Text.Json;

/// <summary>
/// Turns a materialized list into one bounded page. Two limits apply at once: the caller's item
/// count and a character budget, because a hundred chat messages and a hundred project summaries
/// are not remotely the same amount of text.
/// </summary>
internal static class Paging
{
    /// <summary>Matches the built-in file tools' per-result ceiling, so no tool can flood a run on its own.</summary>
    public const int CharacterBudget = 262144;

    public const int DefaultLimit = 100;
    public const int MaxLimit = 1000;

    /// <summary>
    /// The cursor is the offset of the next item, decimal and opaque by contract: callers pass
    /// back what they were given and never compute one.
    /// </summary>
    public static int Offset(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return 0;
        if (!int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) || offset < 0)
            throw new ArgumentException("Cursor is not one this tool produced.", nameof(cursor));
        return offset;
    }

    public static AppReadResult Page<T>(string resource, IReadOnlyList<T> source, string? cursor, int limit, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        var offset = Math.Min(Offset(cursor), source.Count);
        var take = Math.Clamp(limit, 1, MaxLimit);
        var items = new List<JsonElement>();
        var budget = CharacterBudget;
        var truncated = false;
        var index = offset;
        while (index < source.Count && items.Count < take)
        {
            var element = JsonSerializer.SerializeToElement(source[index], options);
            var cost = element.GetRawText().Length;
            // The first item always goes in: a single document larger than the whole budget must
            // still be readable, or the resource would be permanently unreachable.
            if (items.Count > 0 && cost > budget) { truncated = true; break; }
            items.Add(element);
            budget -= cost;
            index++;
        }
        return new AppReadResult(resource, items, index < source.Count ? index.ToString(CultureInfo.InvariantCulture) : null,
            truncated, items.Count, source.Count, null);
    }
}
