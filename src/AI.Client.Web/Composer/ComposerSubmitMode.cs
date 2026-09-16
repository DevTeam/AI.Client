namespace AI.Client.Web.Composer;

/// <summary>
/// What the user asked to do with the composer message. Mapped from the keyboard shortcuts:
/// Enter -> Send, Ctrl+Enter -> Queue, Ctrl+Alt+Enter -> Fork, Ctrl+Shift+Enter -> SendNow.
/// </summary>
public enum ComposerSubmitMode
{
    Send,
    Queue,
    Fork,
    /// <summary>Interrupt whatever the branch is doing and answer this message first.</summary>
    SendNow
}
