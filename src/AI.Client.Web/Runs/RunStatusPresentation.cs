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
    public static string GetStatusClass(ChatRunSnapshot? run) => run switch
    {
        // A property pattern never matches null, so without this explicit case a null run
        // fell through to the `_` arm below and was incorrectly classified as unread instead
        // of "no status at all".
        null => string.Empty,
        { Status: ChatRunStatus.Generating } => "run-status-generating",
        { HasUnreadResponse: false } => string.Empty,
        { Status: ChatRunStatus.Failed } => "run-status-failed",
        { Status: ChatRunStatus.Interrupted or ChatRunStatus.Paused } => "run-status-interrupted",
        _ => "run-status-unread"
    };

    public static string GetStatusTooltip(ChatRunSnapshot run) =>
        run.HasUnreadResponse ? $"{run.Status} · unread response" : run.Status.ToString();
}
