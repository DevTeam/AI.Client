namespace AI.Application.Workspace;

using Contracts.Tools;
using Contracts.Workspace;
using Tools;

/// <summary>
/// Accumulates what a run changed on disk. The aggregate belongs to the run, not to a card in the
/// transcript and not to the model: the model is never asked what it changed, because its answer
/// would be a claim rather than an observation.
/// </summary>
/// <remarks>
/// Implementations capture a baseline copy of a path the first time the run is about to modify it,
/// then compare that baseline against the file as it stands. Two consequences follow, and both are
/// intended: edits to the same file collapse into one net difference, and whatever the user
/// changed before the run started is inside the baseline, so it is never attributed to the agent.
///
/// Nothing here writes to the workspace, and nothing touches a version control index.
/// </remarks>
public interface IWorkspaceChangeTracker
{
    /// <summary>
    /// Starts tracking for a run, retaining any baselines from an interrupted attempt on the same branch.
    /// <paramref name="grants"/> bounds every path this tracker will read.
    /// </summary>
    /// <param name="parent">
    /// The run that delegated this one, when there is one. A subtask keeps baselines of its own so
    /// its work stays attributable, but the person watching the conversation is owed one total:
    /// naming the parent here is what lets the caller's snapshot include what its subtasks did,
    /// while they are still doing it.
    /// </param>
    Task BeginRunAsync(WorkspaceRunKey run, IReadOnlyList<ToolDirectoryGrant> grants, WorkspaceRunKey? parent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Called before a call that may modify the workspace, so a baseline exists to compare against.
    /// </summary>
    Task RecordIntentAsync(WorkspaceRunKey run, ToolDescriptor tool, string arguments, CancellationToken cancellationToken);

    /// <summary>
    /// Called after a call returns. The tracked paths remain available even when the call failed,
    /// because a failed call may still have changed a file.
    /// </summary>
    Task RecordEffectAsync(WorkspaceRunKey run, ToolDescriptor tool, string arguments, CancellationToken cancellationToken);

    /// <summary>
    /// The net change set as it stands, safe to call mid-run. It covers the run's own paths and
    /// those of every run delegated from it, so a caller is never shown a total that omits work it
    /// handed to a subtask.
    /// </summary>
    Task<WorkspaceChangeSet> SnapshotAsync(WorkspaceRunKey run, CancellationToken cancellationToken);

    /// <summary>
    /// Releases the baselines held for a run. A run with a parent hands its baselines up instead of
    /// dropping them: a subtask finishes long before the turn that delegated it, and the caller's
    /// total has to keep covering what it did.
    /// </summary>
    Task CompleteRunAsync(WorkspaceRunKey run, CancellationToken cancellationToken);
}

/// <summary>Identifies the run whose changes are being accumulated.</summary>
public readonly record struct WorkspaceRunKey(Guid ProjectId, Guid ChatId, Guid BranchId);
