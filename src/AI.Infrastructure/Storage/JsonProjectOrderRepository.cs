namespace AI.Infrastructure.Storage;

using System.Text.Json;
using AI.Application.Projects;
using AI.Contracts.FileSystem;

/// <summary>
/// Keeps the project order in <c>project-order.json</c> beside the <c>projects</c> directory, whose
/// every <c>*.json</c> is read as a project. A missing or damaged file reads as no order.
/// </summary>
public sealed class JsonProjectOrderRepository(IFileSystem fileSystem, IUserStorageLocation location)
    : IProjectOrderRepository, IDisposable
{
    private readonly AsyncGate _writes = new();

    private string OrderPath => Path.Combine(location.RootDirectory, "project-order.json");

    public void Dispose() => _writes.Dispose();

    public async Task<IReadOnlyList<Guid>> LoadAsync(CancellationToken cancellationToken)
    {
        var json = await fileSystem.ReadTextAsync(OrderPath, cancellationToken);
        if (json is null) return [];
        try { return JsonSerializer.Deserialize<Guid[]>(json) ?? []; }
        // Only the remembered order is lost; the projects themselves are untouched.
        catch (JsonException) { return []; }
    }

    public async Task SaveAsync(IReadOnlyList<Guid> projectIds, CancellationToken cancellationToken)
    {
        using var lease = await _writes.EnterAsync(cancellationToken);
        await fileSystem.WriteTextAsync(OrderPath + ".tmp", JsonSerializer.Serialize(projectIds.Distinct()), cancellationToken);
        await fileSystem.MoveAsync(OrderPath + ".tmp", OrderPath, true, cancellationToken);
    }
}
