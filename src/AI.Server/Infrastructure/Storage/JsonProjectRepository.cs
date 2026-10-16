namespace AI.Infrastructure.Storage;

using AI.Application.Projects;
using AI.Contracts.FileSystem;
using AI.Contracts.Projects;
using AI.Domain.Projects;

public sealed class JsonProjectRepository(
    IFileSystem fileSystem,
    IProjectStoragePaths paths,
    IProjectDocumentSerializer serializer) : IProjectRepository, IDisposable
{
    public void Dispose() => _writes.Dispose();

    private readonly AsyncGate _writes = new();

    // The last document read or written for each project, by file stamp. A run reads its project at
    // every step to see whether a directory grant changed; while the file is unchanged that read is
    // answered from here instead of from disk. Guarded by _writes.
    private readonly Dictionary<ProjectId, CachedDocument> _documents = [];

    /// <summary>
    /// The project as stored. The document is parsed again for every caller, since a project is
    /// mutable and small; only the read of the file is saved while its stamp is unchanged.
    /// </summary>
    public async Task<StoredProject?> GetAsync(ProjectId id, CancellationToken cancellationToken)
    {
        using var lease = await _writes.EnterAsync(cancellationToken);
        await RecoverAsync(id, cancellationToken);
        var json = await ReadDocumentAsync(id, cancellationToken);
        return json is null ? null : serializer.Deserialize(json);
    }

    // Called only while holding _writes, so nothing this repository writes can land between the
    // stamp and the read.
    private async Task<string?> ReadDocumentAsync(ProjectId id, CancellationToken cancellationToken)
    {
        var path = paths.GetProjectPath(id);
        var stamp = await GetStampAsync(path, cancellationToken);
        if (stamp is null)
        {
            _documents.Remove(id);
            return await fileSystem.ReadTextAsync(path, cancellationToken);
        }
        if (_documents.TryGetValue(id, out var cached) && cached.Stamp == stamp) return cached.Json;
        var json = await fileSystem.ReadTextAsync(path, cancellationToken);
        // Kept only when nothing else wrote the file while it was read.
        if (json is not null && await GetStampAsync(path, cancellationToken) == stamp)
            _documents[id] = new CachedDocument(stamp, json);
        else
            _documents.Remove(id);
        return json;
    }

    private async Task<FileStamp?> GetStampAsync(string path, CancellationToken cancellationToken)
    {
        var entry = await fileSystem.GetEntryAsync(path, cancellationToken);
        return entry is { IsDirectory: false }
            ? new FileStamp(entry.Length, await fileSystem.GetLastWriteTimeAsync(path, cancellationToken)) : null;
    }

    public async Task<IReadOnlyList<StoredProject>> ListAsync(CancellationToken cancellationToken)
    {
        var files = await fileSystem.ListFilesAsync(paths.ProjectsDirectory, "*.json", cancellationToken);
        var projects = new List<StoredProject>();
        foreach (var path in files)
        {
            var json = await fileSystem.ReadTextAsync(path, cancellationToken);
            if (json is not null)
            {
                projects.Add(serializer.Deserialize(json));
            }
        }

        return projects;
    }

    public async Task<ProjectSaveResult> SaveAsync(
        Project project,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        using var lease = await _writes.EnterAsync(cancellationToken);

        await RecoverAsync(project.Id, cancellationToken);
        var projectPath = paths.GetProjectPath(project.Id);
        var currentJson = await ReadDocumentAsync(project.Id, cancellationToken);
        var currentRevision = currentJson is null ? 0 : serializer.Deserialize(currentJson).Revision;
        if (currentRevision != expectedRevision)
        {
            return ProjectSaveResult.Conflict(currentRevision);
        }

        var nextRevision = checked(currentRevision + 1);
        var temporaryPath = paths.GetTemporaryProjectPath(project.Id);
        var json = serializer.Serialize(project, nextRevision);
        _documents.Remove(project.Id);
        await fileSystem.WriteTextAsync(temporaryPath, json, cancellationToken);
        await fileSystem.MoveAsync(temporaryPath, projectPath, true, cancellationToken);
        if (await GetStampAsync(projectPath, cancellationToken) is { } stamp)
            _documents[project.Id] = new CachedDocument(stamp, json);
        return ProjectSaveResult.Saved(nextRevision);
    }

    public async Task<ProjectDeleteResult> DeleteAsync(
        ProjectId id,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        using var lease = await _writes.EnterAsync(cancellationToken);
        await RecoverAsync(id, cancellationToken);
        var projectPath = paths.GetProjectPath(id);
        var currentJson = await fileSystem.ReadTextAsync(projectPath, cancellationToken);
        if (currentJson is null)
        {
            return ProjectDeleteResult.NotFound();
        }

        var currentRevision = serializer.Deserialize(currentJson).Revision;
        if (currentRevision != expectedRevision)
        {
            return ProjectDeleteResult.Conflict(currentRevision);
        }

        _documents.Remove(id);
        await fileSystem.DeleteFileAsync(projectPath, cancellationToken);
        return ProjectDeleteResult.Deleted(currentRevision);
    }

    private async Task RecoverAsync(ProjectId id, CancellationToken cancellationToken)
    {
        var temporaryPath = paths.GetTemporaryProjectPath(id);
        if (!await fileSystem.FileExistsAsync(temporaryPath, cancellationToken))
        {
            return;
        }

        var projectPath = paths.GetProjectPath(id);
        if (await fileSystem.FileExistsAsync(projectPath, cancellationToken))
        {
            await fileSystem.DeleteFileAsync(temporaryPath, cancellationToken);
            return;
        }

        await fileSystem.MoveAsync(temporaryPath, projectPath, false, cancellationToken);
    }

    private sealed record FileStamp(long Length, DateTimeOffset LastWriteTime);

    private sealed record CachedDocument(FileStamp Stamp, string Json);
}
