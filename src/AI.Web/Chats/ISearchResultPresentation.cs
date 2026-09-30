namespace AI.Web.Chats;

using AI.Contracts.Chats;

/// <summary>
/// How the sidebar lays out what a message search found: one group per chat, in the order the
/// server ranked the matches, with the query marked inside each snippet.
/// </summary>
public interface ISearchResultPresentation
{
    /// <summary>
    /// Groups <paramref name="matches"/> by chat. A group sits where its first match was, so a chat
    /// is as high as its best match; the matches inside keep their order.
    /// </summary>
    IReadOnlyList<SearchResultGroup> Group(IReadOnlyList<ChatSearchMatch> matches);

    /// <summary>
    /// Splits <paramref name="snippet"/> into plain and matching runs the same way the server
    /// matched it: literally and ignoring case.
    /// </summary>
    IReadOnlyList<SnippetPart> Highlight(string snippet, string query);
}

/// <param name="Occurrences">Every occurrence in every matching message of the chat.</param>
public sealed record SearchResultGroup(
    Guid ProjectId,
    string ProjectName,
    Guid ChatId,
    string ChatTitle,
    bool IsArchived,
    IReadOnlyList<ChatSearchMatch> Matches,
    int Occurrences);

public readonly record struct SnippetPart(string Text, bool IsMatch);
