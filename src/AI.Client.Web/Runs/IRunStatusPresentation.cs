using AI.Client.Contracts.Runs;

namespace AI.Client.Web.Runs;

/// <summary>
/// Single source of truth for "what color/class does this run's status get" — shared between
/// Home.razor (project/chat/branch-tree rows) and MessageFeed.razor (the in-message branch
/// picker), so a run in the same state always reads the same regardless of which UI surface
/// shows it.
/// </summary>
public interface IRunStatusPresentation
{
    /// <summary>
    /// Indicates a run that cannot make further progress until the user acts. This is a derived
    /// presentation state: an approval can be pending while the run remains Generating.
    /// </summary>
    bool NeedsAttention(ChatRunSnapshot run);

    /// <summary>
    /// Whether the attention state should still be surfaced. A pending tool approval always is —
    /// the run is live-blocked right now and can't proceed until it's resolved. A dormant
    /// attention state (paused, interrupted, or failed-with-recovery) only is until the chat has
    /// been visited: the run's own `Status` stays Paused/Interrupted/Failed until the user
    /// actually resumes/retries it, but the sidebar/badge stop nagging once seen — same rule as
    /// an unread completion.
    /// </summary>
    bool HasVisibleAttention(ChatRunSnapshot run);

    string GetStatusTooltip(ChatRunSnapshot run);

    string GetStatusClass(ChatRunSnapshot? run);
}
