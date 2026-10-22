namespace AI.Application.Workspace;

using Application.Runs;
using AI.Contracts.Runs;

/// <summary>
/// Reads the current runs of the project from the dispatcher and blocks Undo only while one of them
/// is actually writing. See <see cref="IWorkspaceUndoGuard"/> for why a paused run does not count.
/// </summary>
public sealed class WorkspaceUndoGuard(IChatRunDispatcher runs) : IWorkspaceUndoGuard
{
    public async Task<bool> IsProjectWritingAsync(Guid projectId, CancellationToken cancellationToken) =>
        (await runs.GetSnapshotAsync(cancellationToken))
        .Any(run => run.ProjectId == projectId && run.Status == ChatRunStatus.Generating);
}
