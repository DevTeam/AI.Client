namespace AI.Web.Resources;

using AI.Contracts.Workspace;

/// <summary>Keeps the transcript card, docked card and review panel in sync after Undo.</summary>
public sealed class WorkspaceUndoState(IWorkspaceUndoApi api) : IWorkspaceUndoState
{
    private readonly Dictionary<(Guid Project, Guid Chat, Guid Message), WorkspaceUndoStatus> _statuses = [];
    public event Action<Guid, WorkspaceUndoStatus>? Changed;

    public async Task<WorkspaceUndoStatus?> LoadAsync(Guid projectId, Guid chatId, Guid messageId,
        bool refresh = false)
    {
        var key = (projectId, chatId, messageId);
        if (!refresh && _statuses.TryGetValue(key, out var cached)) return cached;
        var status = await api.GetAsync(projectId, chatId, messageId, CancellationToken.None);
        if (status is not null) _statuses[key] = status;
        return status;
    }

    public async Task<WorkspaceUndoStatus?> UndoAsync(Guid projectId, Guid chatId, Guid messageId, string? path)
    {
        var status = await api.UndoAsync(projectId, chatId, messageId, path, CancellationToken.None);
        if (status is null) return null;
        _statuses[(projectId, chatId, messageId)] = status;
        Changed?.Invoke(messageId, status);
        return status;
    }
}
