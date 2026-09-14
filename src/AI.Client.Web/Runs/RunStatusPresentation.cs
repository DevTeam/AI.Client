namespace AI.Client.Web.Runs;

using AI.Client.Contracts.Runs;

/// <summary>
/// Single source of truth for "what color/class does this run's status get" — shared between
/// Home.razor (project/chat/branch-tree rows) and MessageFeed.razor (the in-message branch
/// picker), so a run in the same state always reads the same regardless of which UI surface
/// shows it.
/// </summary>
public static class RunStatusPresentation
{
    /// <summary>
    /// Indicates a run that cannot make further progress until the user acts. This is a derived
    /// presentation state: an approval can be pending while the run remains Generating.
    /// </summary>
    public static bool NeedsAttention(ChatRunSnapshot run) => run switch
    {
        { PendingApproval: not null } => true,
        { Status: ChatRunStatus.Paused or ChatRunStatus.Interrupted } => true,
        { Status: ChatRunStatus.Failed, RecoveryActions: { Count: > 0 } } => true,
        _ => false
    };

    /// <summary>
    /// Whether the attention state should still be surfaced. A pending tool approval always is —
    /// the run is live-blocked right now and can't proceed until it's resolved. A dormant
    /// attention state (paused, interrupted, or failed-with-recovery) only is until the chat has
    /// been visited: the run's own `Status` stays Paused/Interrupted/Failed until the user
    /// actually resumes/retries it, but the sidebar/badge stop nagging once seen — same rule as
    /// an unread completion.
    /// </summary>
    public static bool HasVisibleAttention(ChatRunSnapshot run) =>
        run.PendingApproval is not null || (NeedsAttention(run) && run.HasUnreadResponse);

    public static string GetStatusTooltip(ChatRunSnapshot run) =>
        GetAttentionTooltip(run) ?? GetNonAttentionStatusTooltip(run);

    private static string? GetAttentionTooltip(ChatRunSnapshot run) => run switch
    {
        _ when !HasVisibleAttention(run) => null,
        { PendingApproval: not null } => "Waiting for tool approval",
        { Status: ChatRunStatus.Paused } => "Queue paused - action required",
        { Status: ChatRunStatus.Interrupted } => "Run interrupted - action required",
        { Status: ChatRunStatus.Failed, RecoveryActions: { Count: > 0 } } => "Recovery action required",
        _ => null
    };

    public static string GetStatusClass(ChatRunSnapshot? run) => run switch
    {
        // A property pattern never matches null, so without this explicit case a null run
        // fell through to the `_` arm below and was incorrectly classified as unread instead
        // of "no status at all".
        null => string.Empty,
        _ when HasVisibleAttention(run) => "run-status-attention",
        { Status: ChatRunStatus.Generating } => "run-status-generating",
        { HasUnreadResponse: false } => string.Empty,
        { Status: ChatRunStatus.Failed } => "run-status-failed",
        { Status: ChatRunStatus.Interrupted or ChatRunStatus.Paused } => "run-status-interrupted",
        _ => "run-status-unread"
    };

    private static string GetNonAttentionStatusTooltip(ChatRunSnapshot run) =>
        run.HasUnreadResponse ? $"{run.Status} · unread response" : run.Status.ToString();
}
