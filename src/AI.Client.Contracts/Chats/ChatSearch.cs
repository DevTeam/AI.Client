namespace AI.Client.Contracts.Chats;

/// <summary>
/// What to look for in the stored conversations, and how far to look. Every narrowing field is
/// optional: with none of them set the whole application is searched.
/// </summary>
/// <param name="Query">Literal text unless <paramref name="IsRegex"/> is set.</param>
/// <param name="Roles">
/// Which message roles to consider. Tool results are serialized call output, often tens of
/// kilobytes of JSON apiece, so they are left out unless asked for by name.
/// </param>
/// <param name="Cursor">Opaque continuation from a previous result; callers pass it back unchanged.</param>
public sealed record ChatSearchRequest(
    string Query,
    Guid? ProjectId = null,
    Guid? ChatId = null,
    Guid? BranchId = null,
    bool IsRegex = false,
    bool IgnoreCase = true,
    IReadOnlyList<string>? Roles = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Limit = ChatSearchLimits.DefaultMatches,
    string? Cursor = null);

/// <param name="MatchCount">How many times the query occurs in the whole message, not only in the snippet.</param>
/// <param name="Snippet">Text around the first occurrence, so a long message does not arrive whole.</param>
public sealed record ChatSearchMatch(
    Guid ProjectId,
    string ProjectName,
    Guid ChatId,
    string ChatTitle,
    Guid MessageId,
    Guid? ParentId,
    string Role,
    DateTimeOffset CreatedAt,
    string Snippet,
    int MatchCount);

/// <param name="Truncated">True when a limit ended the search early, so more matches may exist.</param>
/// <param name="NextCursor">Where to resume, or null when the search reached the end.</param>
public sealed record ChatSearchResult(
    IReadOnlyList<ChatSearchMatch> Matches,
    int ChatsSearched,
    int MessagesExamined,
    bool Truncated,
    string? NextCursor,
    string? Error = null);

public static class ChatSearchLimits
{
    public const int DefaultMatches = 50;
    public const int MaxMatches = 500;

    /// <summary>Matches the per-result ceiling the file tools use, so no single read can flood a run.</summary>
    public const int CharacterBudget = 262144;

    /// <summary>
    /// A ceiling on work rather than on output. Messages live one file apiece, so an unbounded
    /// search over a long history is a lot of reading; stopping and saying so beats stalling.
    /// </summary>
    public const int MaxMessagesExamined = 20000;

    public const int SnippetLength = 160;

    public static readonly IReadOnlyList<string> DefaultRoles = ["User", "Assistant"];
}
