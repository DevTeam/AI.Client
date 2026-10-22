namespace AI.Application.Workspace;

using AI.Contracts.Workspace;

/// <summary>Durable before/after bytes for the files changed by one completed turn.</summary>
public interface IWorkspaceUndoService
{
    Task<Guid> CaptureAsync(Guid projectId, Guid chatId, IReadOnlyList<WorkspaceUndoCapture> files,
        CancellationToken cancellationToken);
    Task<WorkspaceUndoStatus?> StatusAsync(Guid projectId, Guid chatId, Guid undoId,
        CancellationToken cancellationToken);
    Task<WorkspaceUndoStatus?> UndoAsync(Guid projectId, Guid chatId, Guid undoId, string? path,
        CancellationToken cancellationToken);
}

public sealed record WorkspaceUndoCapture(string Path, bool BeforeExists, byte[]? Before,
    bool AfterExists, byte[]? After);
