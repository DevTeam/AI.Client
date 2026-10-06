namespace AI.Infrastructure.Workspace;

using System.Security.Cryptography;
using System.Text.Json;
using AI.Application.Projects;
using AI.Application.Resources;
using AI.Application.Workspace;
using AI.Contracts.Projects;
using AI.Contracts.Resources;
using AI.Contracts.Workspace;
using AI.Infrastructure.Storage;

/// <summary>Stores private Undo manifests beside the project's resource assets.</summary>
public sealed class WorkspaceUndoService(IProjectStorageLocation location, ITextFileSystem files,
    IResourceAssetService assets, IProjectService projects, IProjectPathAccess paths)
    : IWorkspaceUndoService, IDisposable
{
    private const long MaximumSnapshotBytes = 64L * 1024 * 1024;
    private readonly AsyncGate _gate = new();

    public void Dispose() => _gate.Dispose();

    public async Task<Guid> CaptureAsync(Guid projectId, Guid chatId, IReadOnlyList<WorkspaceUndoCapture> files,
        CancellationToken cancellationToken)
    {
        var entries = new List<UndoFile>();
        long storedBytes = 0;
        foreach (var file in files)
        {
            var available = (!file.BeforeExists || file.Before is not null)
                && (!file.AfterExists || file.After is not null);
            var bytes = (long)(file.Before?.Length ?? 0) + (file.After?.Length ?? 0);
            if (storedBytes + bytes > MaximumSnapshotBytes) available = false;
            if (available) storedBytes += bytes;
            var before = available && file.BeforeExists
                ? await assets.StoreUndoBytesAsync(projectId, file.Before!, cancellationToken) : null;
            var after = available && file.AfterExists
                ? await assets.StoreUndoBytesAsync(projectId, file.After!, cancellationToken) : null;
            entries.Add(new UndoFile(file.Path, file.BeforeExists, before, file.AfterExists, after, available));
        }
        var id = Guid.CreateVersion7();
        await SaveAsync(new UndoManifest(1, id, projectId, chatId, entries.ToArray()), cancellationToken);
        return id;
    }

    public async Task<WorkspaceUndoStatus?> StatusAsync(Guid projectId, Guid chatId, Guid undoId,
        CancellationToken cancellationToken)
    {
        var manifest = await LoadAsync(projectId, chatId, undoId, cancellationToken);
        return manifest is null ? null : await StatusOfAsync(manifest, cancellationToken);
    }

    public async Task<WorkspaceUndoStatus?> UndoAsync(Guid projectId, Guid chatId, Guid undoId, string? path,
        CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken);
        var manifest = await LoadAsync(projectId, chatId, undoId, cancellationToken);
        if (manifest is null) return null;
        if (path is not null && manifest.Files.All(file => file.Path != path))
            throw new ArgumentException("File is not in this turn's Undo snapshot.");
        var status = await StatusOfAsync(manifest, cancellationToken);
        var targets = status.Files.Where(file => path is null || file.Path == path).ToArray();
        // A bulk Undo is preflighted as a unit. A conflict never silently causes a partial bulk Undo.
        if (targets.Any(file => file.State is WorkspaceUndoFileState.Conflict or WorkspaceUndoFileState.Unavailable))
            return status with { Error = "Some files changed after this turn or cannot be restored." };
        var applied = 0;
        string? errorMessage = null;
        var entries = manifest.Files.ToArray();
        var project = await projects.GetAsync(projectId, cancellationToken);
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            if (path is not null && entry.Path != path || entry.Undone) continue;
            if (await StateOfAsync(project, entry, cancellationToken) != WorkspaceUndoFileState.Ready)
            {
                errorMessage = $"{entry.Path} changed before Undo could write it.";
                break;
            }
            try
            {
                if (entry.BeforeExists)
                {
                    var bytes = await assets.ReadUndoBytesAsync(projectId, entry.BeforeAsset!, cancellationToken);
                    if (bytes is null)
                    {
                        errorMessage = $"The saved original content for {entry.Path} is unavailable.";
                        break;
                    }
                    var directory = Path.GetDirectoryName(entry.Path)!;
                    Directory.CreateDirectory(directory);
                    var temporary = Path.Combine(directory, "." + Path.GetFileName(entry.Path) + ".undo-" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
                        File.Move(temporary, entry.Path, true);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                else File.Delete(entry.Path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                errorMessage = $"Could not restore {entry.Path}: {error.Message}";
                break;
            }
            entries[index] = entry with { Undone = true };
            manifest = manifest with { Files = entries };
            await SaveAsync(manifest, cancellationToken);
            applied++;
        }
        var updated = await StatusOfAsync(manifest, cancellationToken);
        return updated with { AppliedCount = applied, Error = errorMessage };
    }

    private async Task<WorkspaceUndoStatus> StatusOfAsync(UndoManifest manifest, CancellationToken token)
    {
        var statuses = new List<WorkspaceUndoFileStatus>(manifest.Files.Length);
        var project = await projects.GetAsync(manifest.ProjectId, token);
        foreach (var file in manifest.Files)
            statuses.Add(new WorkspaceUndoFileStatus(file.Path,
                await StateOfAsync(project, file, token)));
        return new WorkspaceUndoStatus(manifest.Id, statuses);
    }

    private async Task<WorkspaceUndoFileState> StateOfAsync(ProjectDetails? project, UndoFile file, CancellationToken token)
    {
        if (file.Undone) return WorkspaceUndoFileState.Undone;
        if (!file.Available) return WorkspaceUndoFileState.Unavailable;
        if (project is null || !MayWrite(project, file.Path)) return WorkspaceUndoFileState.Unavailable;
        if (File.Exists(file.Path) != file.AfterExists || Directory.Exists(file.Path))
            return WorkspaceUndoFileState.Conflict;
        if (!file.AfterExists) return WorkspaceUndoFileState.Ready;
        try
        {
            var info = new FileInfo(file.Path);
            if (info.Length > 8 * 1024 * 1024) return WorkspaceUndoFileState.Conflict;
            var bytes = await File.ReadAllBytesAsync(file.Path, token);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return hash == file.AfterAsset ? WorkspaceUndoFileState.Ready : WorkspaceUndoFileState.Conflict;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return WorkspaceUndoFileState.Conflict;
        }
    }

    private bool MayWrite(ProjectDetails project, string path)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path)) return false;
            var full = Path.GetFullPath(path);
            if (!string.Equals(paths.ResolveLinks(full), full, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return false;
            return paths.AccessOf(project, full) == PathAccess.ReadWrite;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException
                                      or NotSupportedException) { return false; }
    }

    private async Task<UndoManifest?> LoadAsync(Guid projectId, Guid chatId, Guid undoId, CancellationToken token)
    {
        var content = await files.ReadTextAsync(PathFor(projectId, undoId), token);
        if (content is null) return null;
        var manifest = JsonSerializer.Deserialize<UndoManifest>(content);
        return manifest is { SchemaVersion: 1 } && manifest.ProjectId == projectId && manifest.ChatId == chatId
            && manifest.Id == undoId ? manifest : null;
    }

    private async Task SaveAsync(UndoManifest manifest, CancellationToken token)
    {
        var path = PathFor(manifest.ProjectId, manifest.Id);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await files.WriteTextAsync(temporary, JsonSerializer.Serialize(manifest), token);
            await files.MoveAsync(temporary, path, true, token);
        }
        finally { await files.DeleteAsync(temporary, CancellationToken.None); }
    }

    private string PathFor(Guid projectId, Guid id) => Path.Combine(location.RootDirectory, "assets",
        projectId.ToString("N"), "undo", id.ToString("N") + ".json");

    private sealed record UndoManifest(int SchemaVersion, Guid Id, Guid ProjectId, Guid ChatId,
        UndoFile[] Files);
    private sealed record UndoFile(string Path, bool BeforeExists, string? BeforeAsset,
        bool AfterExists, string? AfterAsset, bool Available, bool Undone = false);
}
