namespace AI.Application.Workspace;

/// <summary>Decides whether a workspace Undo may run right now.</summary>
public interface IWorkspaceUndoGuard
{
    /// <summary>
    /// Whether the project is writing files at this moment. Undo restores the pre-turn bytes of one
    /// chat's turn, but the workspace is shared by every chat of the project, so a run that is
    /// generating right now can still write a file the Undo is about to replace.
    /// <para>
    /// A paused run is not writing: its worker is stopped, and pause is a standing state, not an
    /// activity. A chat left with a queued message stays paused for days, so counting it as busy
    /// made Undo refuse every turn of every chat in that project until someone resumed or cleared
    /// that unrelated queue. The per-file check in <see cref="IWorkspaceUndoService"/> remains the
    /// real guard against a file that changed in the meantime.
    /// </para>
    /// </summary>
    Task<bool> IsProjectWritingAsync(Guid projectId, CancellationToken cancellationToken);
}
