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
/// that is easy to get wrong (entering history with text in the field, walking past either end,
/// resuming a stale index after the chat changed underneath). The contract is the part the page
/// actually consumes; the implementation lives in <see cref="ComposerHistoryNavigator"/>.
/// </remarks>
public sealed class ComposerHistoryNavigator : IComposerHistoryNavigator
{
    /// <summary>Position in the history list (0 = newest), or null when the composer is not browsing.</summary>
    public int? Index { get; private set; }

    public bool IsActive => Index is not null;

    /// <summary>
    /// Moves one entry towards older messages. Returns the text to show, or null when the press
    /// changes nothing and the composer should be left alone: the field already holds the user's
    /// own text, the history is empty, or the oldest entry is already showing.
    /// </summary>
    public string? MoveOlder(IReadOnlyList<string> history, string currentText)
    {
        // History is entered from an empty field only: with text in it the Up key belongs to the
        // caret, and swapping what the user is writing for an older message would lose that text.
        // The check guards entering history, not walking it — while an entry is on screen the field
        // holds that entry, and another Up has to keep going back.
        if (!IsActive && currentText.Length > 0) return null;
        if (history.Count == 0) return null;
        if (Index is not { } index)
        {
            Index = 0;
            return history[0];
        }

        // Stop at the oldest entry instead of wrapping: wrapping would silently jump the user
        // from the far end back to an empty field, which reads as "my text disappeared".
        var next = index + 1;
        if (next >= history.Count) return null;
        Index = next;
        return history[next];
    }

    /// <summary>
    /// Moves one entry towards newer messages, clearing the field once it steps past the newest
    /// entry — the field was empty when browsing started, so that is what it returns to. Null when
    /// not browsing at all.
    /// </summary>
    public string? MoveNewer(IReadOnlyList<string> history)
    {
        if (Index is not { } index) return null;
        var next = index - 1;

        // The list can have shrunk (or been swapped for another project's) since browsing
        // started; clamp rather than throw.
        if (next >= history.Count) next = history.Count - 1;
        if (next < 0)
        {
            Index = null;
            return string.Empty;
        }

        Index = next;
        return history[next];
    }

    /// <summary>Leaves history and returns the empty text to put back, or null when not browsing.</summary>
    public string? Exit()
    {
        if (Index is null) return null;
        Index = null;
        return string.Empty;
    }

    /// <summary>
    /// Drops the browsing position without restoring anything — for when the composer's content
    /// stops being a history entry anyway: the user edited it, sent it, or switched chat.
    /// </summary>
    public void Reset() => Index = null;
}
