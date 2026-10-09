namespace AI.Web.Resources;

using AI.Contracts.Workspace;

public interface IWorkspaceUndoState
{
    event Action<Guid, WorkspaceUndoStatus>? Changed;
    Task<WorkspaceUndoStatus?> LoadAsync(Guid projectId, Guid chatId, Guid messageId, Guid undoId, bool refresh = false);
    Task<WorkspaceUndoStatus?> UndoAsync(Guid projectId, Guid chatId, Guid messageId, string? path);
}
