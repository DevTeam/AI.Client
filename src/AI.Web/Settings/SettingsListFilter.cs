namespace AI.Web.Settings;

using Chats;

public sealed class SettingsListFilter : ISettingsListFilter
{
    private const StringComparison IgnoreCase = StringComparison.CurrentCultureIgnoreCase;

    public IReadOnlyList<string> Words(string query) =>
        query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool Matches(string query, params string?[] fields)
    {
        var words = Words(query);
        return words.Count == 0 || words.All(word => fields.Any(field => field?.Contains(word, IgnoreCase) == true));
    }

    public IReadOnlyList<SnippetPart> Highlight(string text, string query)
    {
        var words = Words(query);
        if (words.Count == 0 || text.Length == 0) return [new SnippetPart(text, false)];
        // Marks every occurrence of every word, then merges overlaps, so "ab b" over "abc" is one run.
        var marked = new bool[text.Length];
        foreach (var word in words)
        {
            for (var index = text.IndexOf(word, IgnoreCase); index >= 0; index = text.IndexOf(word, index + 1, IgnoreCase))
                Array.Fill(marked, true, index, Math.Min(word.Length, text.Length - index));
        }
        var parts = new List<SnippetPart>();
        var start = 0;
        for (var index = 1; index <= text.Length; index++)
        {
            if (index < text.Length && marked[index] == marked[start]) continue;
            parts.Add(new SnippetPart(text[start..index], marked[start]));
            start = index;
        }
        return parts;
    }
}
