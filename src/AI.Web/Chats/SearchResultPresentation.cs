namespace AI.Web.Chats;

using System.Text.RegularExpressions;
using AI.Contracts.Chats;

public sealed class SearchResultPresentation : ISearchResultPresentation
{
    // What a snippet loses to read as a sentence: a link keeps its text, a heading mark goes, and
    // so do the marks of code and bold. Single *, _ and > stay: in a snippet, where line breaks
    // are already gone, they are far more often a sign or a snake_case name than markup.
    private readonly Regex _link = new(@"!?\[([^\]\[]*)\]\([^)\s]*\)", RegexOptions.CultureInvariant);
    private readonly Regex _lineMark = new(@"(?<=^|\s)#{1,6}\s+", RegexOptions.CultureInvariant);
    private readonly Regex _inlineMark = new(@"`+|\*\*|__|~~", RegexOptions.CultureInvariant);

    public IReadOnlyList<SearchResultGroup> Group(IReadOnlyList<ChatSearchMatch> matches) =>
        matches
            .GroupBy(match => match.ChatId)
            .Select(group =>
            {
                var first = group.First();
                return new SearchResultGroup(first.ProjectId, first.ProjectName, first.ChatId, first.ChatTitle,
                    first.IsArchived, group.ToArray(), group.Sum(match => match.MatchCount));
            })
            .ToArray();

    public IReadOnlyList<SnippetPart> Highlight(string snippet, string query)
    {
        var needle = query.Trim();
        snippet = WithoutMarkup(snippet, needle);
        if (needle.Length == 0 || snippet.Length == 0) return [new SnippetPart(snippet, false)];
        var parts = new List<SnippetPart>();
        var cursor = 0;
        var index = snippet.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            if (index > cursor) parts.Add(new SnippetPart(snippet[cursor..index], false));
            parts.Add(new SnippetPart(snippet.Substring(index, needle.Length), true));
            cursor = index + needle.Length;
            index = snippet.IndexOf(needle, cursor, StringComparison.OrdinalIgnoreCase);
        }

        if (cursor < snippet.Length) parts.Add(new SnippetPart(snippet[cursor..], false));
        return parts;
    }

    /// <summary>
    /// The snippet as prose. A query that is itself markup, or runs across it, keeps the snippet
    /// as written: a result must still show what it matched.
    /// </summary>
    private string WithoutMarkup(string snippet, string needle)
    {
        var plain = _inlineMark.Replace(_lineMark.Replace(_link.Replace(snippet, "$1"), string.Empty), string.Empty);
        return needle.Length == 0 || plain.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || !snippet.Contains(needle, StringComparison.OrdinalIgnoreCase)
            ? plain
            : snippet;
    }
}
