namespace AI.Web.Composer;

/// <summary>
/// What the user asked to do with the composer message. Mapped from the keyboard shortcuts:
/// Enter -> Send, Ctrl+Enter -> Queue, Ctrl+Alt+Enter -> Fork.
/// </summary>
public enum ComposerSubmitMode
{
    /// <summary>Start a turn, or add to the turn the branch is generating.</summary>
    Send,
    Queue,
    Fork
}
