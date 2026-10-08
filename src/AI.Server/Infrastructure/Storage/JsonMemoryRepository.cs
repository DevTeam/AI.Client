namespace AI.Infrastructure.Storage;

using System.Text.Json;
using AI.Application.Memory;
using AI.Contracts.FileSystem;
using AI.Contracts.Memory;

/// <summary>
/// The person's memory in <c>memory/user.json</c> and each project's in
/// <c>memory/projects/{projectId}.json</c>. One gate covers all catalogs: they are small and
/// written rarely, so a single boundary is simpler than one lock per file.
/// </summary>
public sealed class JsonMemoryRepository(IProjectStorageLocation location, IFileSystem files) : IMemoryRepository, IDisposable
{
    private readonly AsyncGate _gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public void Dispose() => _gate.Dispose();

    public async Task<IReadOnlyList<MemoryEntry>> ListAsync(Guid? projectId, CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken);
        return await LoadAsync(projectId, cancellationToken);
    }

    public async Task<MemoryWriteResult> AddAsync(Guid? projectId, MemoryEntry entry, int capacity,
        CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken);
        var entries = (await LoadAsync(projectId, cancellationToken)).ToList();
        if (entries.Count >= capacity)
            return MemoryWriteResult.Rejected($"This memory already holds {capacity} entries; update or delete one first.");
        entries.Add(entry);
        await SaveAsync(projectId, entries, cancellationToken);
        return MemoryWriteResult.Saved(entry);
    }

    public async Task<MemoryWriteResult> UpdateAsync(Guid? projectId, Guid id, long expectedRevision,
        Func<MemoryEntry, MemoryEntry> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        using var lease = await _gate.EnterAsync(cancellationToken);
        var entries = (await LoadAsync(projectId, cancellationToken)).ToList();
        var index = entries.FindIndex(item => item.Id == id);
        if (index < 0) return MemoryWriteResult.NotFound();
        if (entries[index].Revision != expectedRevision) return MemoryWriteResult.Conflict(entries[index]);
        var changed = change(entries[index]) with { Id = id, Scope = entries[index].Scope, ProjectId = entries[index].ProjectId };
        entries[index] = changed;
        await SaveAsync(projectId, entries, cancellationToken);
        return MemoryWriteResult.Saved(changed);
    }

    public async Task<MemoryWriteResult> DeleteAsync(Guid? projectId, Guid id, long expectedRevision,
        CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken);
        var entries = (await LoadAsync(projectId, cancellationToken)).ToList();
        var index = entries.FindIndex(item => item.Id == id);
        if (index < 0) return MemoryWriteResult.NotFound();
        var current = entries[index];
        if (current.Revision != expectedRevision) return MemoryWriteResult.Conflict(current);
        entries.RemoveAt(index);
        await SaveAsync(projectId, entries, cancellationToken);
        return MemoryWriteResult.Saved(current);
    }

    public async Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken);
        await files.DeleteFileAsync(PathFor(projectId), cancellationToken);
    }

    private async Task<IReadOnlyList<MemoryEntry>> LoadAsync(Guid? projectId, CancellationToken token)
    {
        var json = await files.ReadTextAsync(PathFor(projectId), token);
        if (json is null) return [];
        var document = JsonSerializer.Deserialize<MemoryDocument>(json)
            ?? throw new JsonException("Memory catalog is empty.");
        if (document.SchemaVersion != 1) throw new JsonException("Unsupported memory catalog schema.");
        return document.Entries;
    }

    private async Task SaveAsync(Guid? projectId, IReadOnlyList<MemoryEntry> entries, CancellationToken token)
    {
        var path = PathFor(projectId);
        var temporary = path + ".tmp";
        await files.WriteTextAsync(temporary, JsonSerializer.Serialize(new MemoryDocument(1, entries), Json), token);
        await files.MoveAsync(temporary, path, true, token);
    }

    private string PathFor(Guid? projectId) => projectId is { } id
        ? Path.Combine(location.RootDirectory, "memory", "projects", $"{id}.json")
        : Path.Combine(location.RootDirectory, "memory", "user.json");

    private sealed record MemoryDocument(int SchemaVersion, IReadOnlyList<MemoryEntry> Entries);
}
