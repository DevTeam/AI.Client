namespace AI.Web.Composer;

// CA1716: `Exit` matches the existing public method on the concrete `ComposerHistoryNavigator`,
// and renaming the interface member would split the contract into "navigator-side name" vs.
// "call-site name" for no language the project actually targets.
#pragma warning disable CA1716

/// <summary>
/// Where the composer currently sits while the user walks back through previously sent messages
/// with Up/Down. Exposed as an interface so the page can depend on the state machine, not on the
/// concrete class, and so a fake can stand in for it in tests that exercise the page itself.
/// </summary>
/// <remarks>
/// Pure state machine on purpose: the page owns the textarea, the storage and the JS interop,
/// and none of that is needed to decide which entry Up/Down should land on — which is the part
/// that is easy to get wrong (losing the draft, walking past either end, resuming a stale index
/// after the chat changed underneath). The contract is the part the page actually consumes; the
/// implementation lives in <see cref="ComposerHistoryNavigator"/>.
/// </remarks>
public interface IComposerHistoryNavigator
{
    /// <summary>Position in the history list (0 = newest), or null when the composer is not browsing.</summary>
    int? Index { get; }

    /// <summary>Whether the composer is currently showing a history entry rather than its own text.</summary>
    bool IsActive { get; }

    /// <summary>
    /// Moves one entry towards older messages. Returns the text to show, or null when there is
    /// nothing to move to (empty history, or already at the oldest entry) and the composer
    /// should be left alone.
    /// </summary>
    string? MoveOlder(IReadOnlyList<string> history, string currentText);

    /// <summary>
    /// Moves one entry towards newer messages, returning the draft the user started from once it
    /// steps past the newest entry. Null when not browsing at all.
    /// </summary>
    string? MoveNewer(IReadOnlyList<string> history);

    /// <summary>Leaves history and returns the draft to restore, or null when not browsing.</summary>
    string? Exit();

    /// <summary>
    /// Drops the browsing position without restoring anything — for when the composer's content
    /// stops being a history entry anyway: the user edited it, sent it, or switched chat.
    /// </summary>
    void Reset();
}

#pragma warning restore CA1716
