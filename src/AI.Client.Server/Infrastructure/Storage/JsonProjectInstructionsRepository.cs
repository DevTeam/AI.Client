namespace AI.Client.Infrastructure.Storage;

using System.Text.Json;
using AI.Client.Application.Instructions;
using AI.Client.Contracts.Instructions;

/// <summary>
/// One document per project in <c>instructions/projects/{projectId}.json</c>, kept apart from the
/// project document so editing instructions never races a security or connection change.
/// </summary>
public sealed class JsonProjectInstructionsRepository(IProjectStorageLocation location, ITextFileSystem files)
    : IProjectInstructionsRepository, IDisposable
{
    private readonly AsyncGate _gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public void Dispose() => _gate.Dispose();

    public async Task<ProjectInstructions> GetAsync(Guid projectId, CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken);
        return await LoadAsync(projectId, cancellationToken);
    }

    public async Task<(bool Saved, ProjectInstructions Current)> SaveAsync(Guid projectId, string text,
        bool includeWorkspaceFiles, long expectedRevision, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken);
        var current = await LoadAsync(projectId, cancellationToken);
        if (current.Revision != expectedRevision) return (false, current);
        var saved = new ProjectInstructions(projectId, text, includeWorkspaceFiles, checked(current.Revision + 1), updatedAt);
        var path = PathFor(projectId);
        var temporary = path + ".tmp";
        await files.WriteTextAsync(temporary, JsonSerializer.Serialize(
            new InstructionsDocument(1, saved.Text, saved.IncludeWorkspaceFiles, saved.Revision, updatedAt), Json), cancellationToken);
        await files.MoveAsync(temporary, path, true, cancellationToken);
        return (true, saved);
    }

    public async Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken);
        await files.DeleteAsync(PathFor(projectId), cancellationToken);
    }

    private async Task<ProjectInstructions> LoadAsync(Guid projectId, CancellationToken token)
    {
        var json = await files.ReadTextAsync(PathFor(projectId), token);
        if (json is null) return new ProjectInstructions(projectId, string.Empty, true, 0, null);
        var document = JsonSerializer.Deserialize<InstructionsDocument>(json)
            ?? throw new JsonException("Project instructions document is empty.");
        if (document.SchemaVersion != 1) throw new JsonException("Unsupported project instructions schema.");
        return new ProjectInstructions(projectId, document.Text, document.IncludeWorkspaceFiles, document.Revision, document.UpdatedAt);
    }

    private string PathFor(Guid projectId) =>
        Path.Combine(location.RootDirectory, "instructions", "projects", $"{projectId}.json");

    private sealed record InstructionsDocument(int SchemaVersion, string Text, bool IncludeWorkspaceFiles, long Revision,
        DateTimeOffset UpdatedAt);
}
