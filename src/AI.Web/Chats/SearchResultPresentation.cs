namespace AI.Web.Chats;

using AI.Contracts.Chats;

public sealed class SearchResultPresentation : ISearchResultPresentation
{
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
}
