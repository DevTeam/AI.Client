namespace AI.Web.Composer;

/// <summary>
/// What the composer's JS side needs after an Up/Down press: the text to put in the textarea and
/// whether the composer is still browsing history (which decides who owns the next Escape).
/// </summary>
public sealed record ComposerHistoryMove(string Text, bool Active);

/// <summary>
/// Where the composer currently sits while the user walks back through previously sent messages
/// with Up/Down. Index 0 is the newest entry; "not active" means the composer holds the user's
/// own text, not a history entry.
/// </summary>
/// <remarks>
/// Pure state machine on purpose: the page owns the textarea, the storage and the JS interop,
/// and none of that is needed to decide which entry Up/Down should land on — which is the part
/// that is easy to get wrong (losing the draft, walking past either end, resuming a stale index
/// after the chat changed underneath).
/// </remarks>
public sealed class ComposerHistoryNavigator : IComposerHistoryNavigator
{
    // The text the composer held when browsing started. Kept here rather than in the drafts
    // store because browsing must not overwrite the saved draft: walking through history and
    // then switching project has to leave the unsent text exactly as it was.
    private string _draft = string.Empty;

    /// <summary>Position in the history list (0 = newest), or null when the composer is not browsing.</summary>
    public int? Index { get; private set; }

    public bool IsActive => Index is not null;

    /// <summary>
    /// Moves one entry towards older messages. Returns the text to show, or null when there is
    /// nothing to move to (empty history, or already at the oldest entry) and the composer
    /// should be left alone.
    /// </summary>
    public string? MoveOlder(IReadOnlyList<string> history, string currentText)
    {
        if (history.Count == 0) return null;
        if (Index is not { } index)
        {
            _draft = currentText;
            Index = 0;
            return history[0];
        }

        // Stop at the oldest entry instead of wrapping: wrapping would silently jump the user
        // from the far end back to their own draft, which reads as "my text disappeared".
        var next = index + 1;
        if (next >= history.Count) return null;
        Index = next;
        return history[next];
    }

    /// <summary>
    /// Moves one entry towards newer messages, returning the draft the user started from once it
    /// steps past the newest entry. Null when not browsing at all.
    /// </summary>
    public string? MoveNewer(IReadOnlyList<string> history)
    {
        if (Index is not { } index) return null;
        var next = index - 1;
        if (next < 0)
        {
            Index = null;
            return _draft;
        }

        // The list can have shrunk (or been swapped for another project's) since browsing
        // started; clamp rather than throw.
        if (next >= history.Count) next = history.Count - 1;
        if (next < 0)
        {
            Index = null;
            return _draft;
        }

        Index = next;
        return history[next];
    }

    /// <summary>Leaves history and returns the draft to restore, or null when not browsing.</summary>
    public string? Exit()
    {
        if (Index is null) return null;
        Index = null;
        return _draft;
    }

    /// <summary>
    /// Drops the browsing position without restoring anything — for when the composer's content
    /// stops being a history entry anyway: the user edited it, sent it, or switched chat.
    /// </summary>
    public void Reset()
    {
        Index = null;
        _draft = string.Empty;
    }
}
