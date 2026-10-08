namespace AI.Infrastructure.Storage;

using AI.Application.Projects;
using AI.Application.Skills;
using AI.Contracts.FileSystem;
using AI.Contracts.Skills;
using Pure.DI;

/// <summary>Built-in, user and project SKILL.md files. Writes replace one file atomically.</summary>
public sealed class SkillCatalog([Tag("built-in")] ISkillCatalog builtIns, IProjectStorageLocation location,
    IFileSystem files, IProjectService projects) : ISkillCatalog, IDisposable
{
    private readonly AsyncGate _gate = new();

    public void Dispose() => _gate.Dispose();
    public IReadOnlyList<SkillDefinition> List() => builtIns.List();
    public SkillDefinition? GetById(string id) => builtIns.GetById(id);

    public async Task<IReadOnlyList<SkillDefinition>> ListAsync(Guid? projectId, CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken);
        var user = await LoadScopeAsync("User", null, cancellationToken);
        var project = projectId is { } id
            ? await LoadScopeAsync("Project", id, cancellationToken) : [];
        return [.. builtIns.List(), .. user, .. project];
    }

    public async Task<SkillDefinition?> GetByIdAsync(string id, Guid? projectId, CancellationToken cancellationToken) =>
        (await ListAsync(projectId, cancellationToken))
        .Where(skill => skill.Id == id)
        .OrderByDescending(skill => skill.Source == "Project" ? 2 : skill.Source == "User" ? 1 : 0)
        .FirstOrDefault();

    public async Task<SkillWriteResult> SaveAsync(SkillWriteRequest request, CancellationToken cancellationToken)
    {
        if (request.Scope is not ("User" or "Project"))
            return new("Rejected", Error: "Choose User or Project scope.");
        if (request.Scope == "Project" && (request.ProjectId is null
            || await projects.GetAsync(request.ProjectId.Value, cancellationToken) is null))
            return new("Rejected", Error: "Project not found.");
        if (request.Scope == "User" && request.ProjectId is not null)
            return new("Rejected", Error: "A user skill cannot name a project.");
        SkillDefinition parsed;
        try { parsed = SkillMarkdown.Parse(request.Content, request.Scope, request.ProjectId); }
        catch (Exception error) when (error is ArgumentException or FormatException or System.Text.Json.JsonException)
        { return new("Rejected", Error: error.Message); }
        if (builtIns.GetById(parsed.Id) is not null)
            return new("Rejected", Error: "A bundled skill already uses this ID.");

        using var lease = await _gate.EnterAsync(cancellationToken);
        var own = await LoadScopeAsync(request.Scope, request.ProjectId, cancellationToken);
        var existing = own.SingleOrDefault(skill => skill.Id == parsed.Id);
        if ((existing?.Revision ?? 0) != request.Revision)
            return new("Conflict", existing, "The skill revision changed. Reload it before saving.");
        var revision = checked(request.Revision + 1);
        var savedContent = SkillMarkdown.WithMetadata(request.Content, revision, request.Enabled);
        var saved = SkillMarkdown.Parse(savedContent, request.Scope, request.ProjectId);
        var path = PathFor(request.Scope, request.ProjectId, saved.Id);
        await files.WriteTextAsync(path + ".tmp", savedContent, cancellationToken);
        await files.MoveAsync(path + ".tmp", path, true, cancellationToken);
        return new("Saved", saved);
    }

    public async Task<SkillWriteResult> DeleteAsync(string id, string scope, Guid? projectId, long revision,
        CancellationToken cancellationToken)
    {
        if (scope is not ("User" or "Project") || scope == "Project" && projectId is null)
            return new("Rejected", Error: "Choose a writable skill scope.");
        using var lease = await _gate.EnterAsync(cancellationToken);
        var current = (await LoadScopeAsync(scope, projectId, cancellationToken))
            .SingleOrDefault(skill => skill.Id == id);
        if (current is null) return new("NotFound");
        if (current.Revision != revision)
            return new("Conflict", current, "The skill revision changed. Reload it before deleting.");
        await files.DeleteFileAsync(PathFor(scope, projectId, id), cancellationToken);
        return new("Deleted", current);
    }

    public async Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken);
        foreach (var file in await files.ListFilesRecursivelyAsync(DirectoryFor("Project", projectId),
                     "SKILL.md", cancellationToken))
            await files.DeleteFileAsync(file, cancellationToken);
    }

    private async Task<IReadOnlyList<SkillDefinition>> LoadScopeAsync(string scope, Guid? projectId,
        CancellationToken cancellationToken)
    {
        if (scope == "Project" && projectId is null) return [];
        var root = DirectoryFor(scope, projectId);
        var paths = await files.ListFilesRecursivelyAsync(root, "SKILL.md", cancellationToken);
        var skills = new List<SkillDefinition>();
        foreach (var path in paths)
        {
            var id = Path.GetFileName(Path.GetDirectoryName(path));
            if (string.IsNullOrEmpty(id)) continue;
            if (path != PathFor(scope, projectId, id)) continue;
            var content = await files.ReadTextAsync(path, cancellationToken);
            if (content is not null) skills.Add(SkillMarkdown.Parse(content, scope, projectId));
        }
        return skills.OrderBy(skill => skill.Name, StringComparer.Ordinal).ToArray();
    }

    private string DirectoryFor(string scope, Guid? projectId) => scope == "User"
        ? Path.Combine(location.RootDirectory, "skills", "user")
        : Path.Combine(location.RootDirectory, "skills", "projects", projectId!.Value.ToString());

    private string PathFor(string scope, Guid? projectId, string id) =>
        Path.Combine(DirectoryFor(scope, projectId), id, "SKILL.md");
}
