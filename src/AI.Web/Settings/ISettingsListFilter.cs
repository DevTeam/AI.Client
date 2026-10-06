namespace AI.Web.Settings;

using Chats;

/// <summary>
/// How the lists of the settings drawers (Connections, MCP, Memory, Skills) narrow to a typed
/// filter: every word of it, ignoring case, somewhere in what the row is about, in any order.
/// </summary>
public interface ISettingsListFilter
{
    /// <summary>The query as it is matched: its words, without blanks; empty matches every row.</summary>
    IReadOnlyList<string> Words(string query);

    /// <summary>
    /// Whether each word of <paramref name="query"/> occurs in at least one of <paramref name="fields"/>,
    /// so "git push" finds a row named "Push" whose description mentions git.
    /// </summary>
    bool Matches(string query, params string?[] fields);

    /// <summary>Splits <paramref name="text"/> into plain runs and runs that some word of the query matched.</summary>
    IReadOnlyList<SnippetPart> Highlight(string text, string query);
}
