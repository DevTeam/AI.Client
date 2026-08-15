namespace AI.Client.Web.Composer;

/// <summary>
/// What the user asked to do with the composer message. Mapped from the keyboard shortcuts:
/// Enter -> Send, Ctrl+Enter -> Queue, Ctrl+Alt+Enter -> Fork.
/// </summary>
public enum ComposerSubmitMode
{
    Send,
    Queue,
    Fork
}
