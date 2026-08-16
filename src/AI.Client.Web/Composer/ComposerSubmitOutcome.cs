namespace AI.Client.Web.Composer;

using AI.Client.Contracts.Chats;
using AI.Client.Contracts.Runs;

/// <summary>
/// Result of submitting a composer message. The component uses this to know whether to clear the
/// textarea, refresh the chat list, scroll, etc. — i.e. what UI follow-ups are required.
/// </summary>
public abstract record ComposerSubmitOutcome
{
    private ComposerSubmitOutcome() { }

    /// <summary>The message was not enqueued; nothing changed. The UI should surface <see cref="Reason"/>.</summary>
    public sealed record Rejected(string Reason) : ComposerSubmitOutcome;

    /// <summary>The message was enqueued successfully. The component should clear the textarea, refresh chats, etc.</summary>
    public sealed record Accepted(
        ChatDetails Chat,
        ChatRunSnapshot Snapshot,
        Guid RunBranchId,
        Guid? UpdatedBranchLeafId,
        // ReSharper disable once NotAccessedPositionalProperty.Global
        bool HeldInQueue) : ComposerSubmitOutcome;
}
