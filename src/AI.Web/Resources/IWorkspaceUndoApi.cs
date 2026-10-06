namespace AI.Web.Resources;

using AI.Contracts.Workspace;

public interface IWorkspaceUndoApi
{
    Task<WorkspaceUndoStatus?> GetAsync(Guid projectId, Guid chatId, Guid messageId, CancellationToken token);
    Task<WorkspaceUndoStatus?> UndoAsync(Guid projectId, Guid chatId, Guid messageId, string? path,
        CancellationToken token);
}
