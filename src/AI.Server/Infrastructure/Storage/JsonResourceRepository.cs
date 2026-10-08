namespace AI.Infrastructure.Storage;

using System.Text.Json;
using AI.Application.Resources;
using AI.Contracts.FileSystem;
using AI.Contracts.Resources;

/// <summary>Small per-project resource catalog. Immutable chat turns keep their own reference snapshots.</summary>
public sealed class JsonResourceRepository(IProjectStorageLocation location, IFileSystem files) : IResourceRepository, IDisposable
{
    private readonly AsyncGate _gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly StringComparison Comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public void Dispose() => _gate.Dispose();

    public async Task<IReadOnlyList<ResourceDefinition>> ListAsync(Guid projectId, CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken);
        return await LoadAsync(projectId, cancellationToken);
    }

    public async Task<ResourceDefinition> GetOrCreateAsync(Guid projectId, ChatResource reference,
        CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken);
        var entries = (await LoadAsync(projectId, cancellationToken)).ToList();
        var existing = entries.FirstOrDefault(item => !item.Retired && item.Reference.Kind == reference.Kind
            && string.Equals(item.Reference.Path, reference.Path, Comparison));
        if (existing is not null) return existing;
        var created = new ResourceDefinition(reference, 1, false);
        entries.Add(created);
        await SaveAsync(projectId, entries, cancellationToken);
        return created;
    }

    public async Task<ResourceDefinition?> RetireAsync(Guid projectId, Guid id, long expectedRevision,
        CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken);
        var entries = (await LoadAsync(projectId, cancellationToken)).ToList();
        var index = entries.FindIndex(item => item.Reference.Id == id);
        if (index < 0) return null;
        var current = entries[index];
        if (current.Revision != expectedRevision) throw new InvalidOperationException("Resource revision changed.");
        if (current.Retired) return current;
        var retired = current with { Revision = checked(current.Revision + 1), Retired = true };
        entries[index] = retired;
        await SaveAsync(projectId, entries, cancellationToken);
        return retired;
    }

    public async Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken);
        await files.DeleteFileAsync(PathFor(projectId), cancellationToken);
    }

    private async Task<IReadOnlyList<ResourceDefinition>> LoadAsync(Guid projectId, CancellationToken token)
    {
        var json = await files.ReadTextAsync(PathFor(projectId), token);
        if (json is null) return [];
        var document = JsonSerializer.Deserialize<ResourceDocument>(json)
            ?? throw new JsonException("Resource catalog is empty.");
        if (document.SchemaVersion != 1) throw new JsonException("Unsupported resource catalog schema.");
        return document.Resources;
    }

    private async Task SaveAsync(Guid projectId, IReadOnlyList<ResourceDefinition> entries, CancellationToken token)
    {
        var path = PathFor(projectId);
        var temporary = path + ".tmp";
        await files.WriteTextAsync(temporary, JsonSerializer.Serialize(new ResourceDocument(1, entries), Json), token);
        await files.MoveAsync(temporary, path, true, token);
    }

    private string PathFor(Guid projectId) => Path.Combine(location.RootDirectory, "resources", $"{projectId}.json");
    private sealed record ResourceDocument(int SchemaVersion, IReadOnlyList<ResourceDefinition> Resources);
}
