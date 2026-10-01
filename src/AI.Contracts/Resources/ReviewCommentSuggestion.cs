namespace AI.Contracts.Resources;

/// <summary>
/// Asks the Host to draft a review comment on a fragment the user is commenting: text of a chat
/// message (<paramref name="MessageId"/> and the selected <paramref name="Quote"/>) or lines of a
/// changed file (<paramref name="Path"/>, the lines in <paramref name="Quote"/> and the diff around
/// them in <paramref name="Diff"/>). <paramref name="Automatic"/> is a draft offered unasked, which
/// the Host writes only while Settings → Chat allows it; without it the user asked for one.
/// </summary>
public sealed record ReviewCommentSuggestionRequest(
    string Quote,
    Guid? MessageId = null,
    string? Path = null,
    string? Diff = null,
    bool Automatic = false);

/// <summary>A draft of the user's review comment; the user saves it only if it fits.</summary>
public sealed record ReviewCommentSuggestion(string Text);
