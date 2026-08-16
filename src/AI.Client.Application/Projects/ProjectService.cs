// ReSharper disable UseCollectionExpression
namespace AI.Client.Application.Projects;

using Settings;
using AI.Client.Contracts.Projects;
using AI.Client.Domain.Projects;

public sealed class ProjectService(
    IProjectRepository repository,
    IIdGenerator idGenerator,
    IClock clock,
    IGlobalSettingsRepository globalSettingsRepository) : IProjectService
{
    public async Task<IReadOnlyList<ProjectSummary>> ListAsync(CancellationToken cancellationToken) =>
        (await repository.ListAsync(cancellationToken))
        .OrderBy(item => item.Project.Name, StringComparer.OrdinalIgnoreCase)
        .Select(item => ToSummary(item.Project, item.Revision))
        .ToArray();

    public async Task<ProjectDetails?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var projectId = new ProjectId(id);
        var stored = await repository.GetAsync(projectId, cancellationToken);
        return stored is not null ? ToDetails(stored.Project, stored.Revision) : null;
    }

    public async Task<ProjectDetails> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var project = new Project(new ProjectId(idGenerator.Create()), request.Name, request.Description, clock.UtcNow);
        var defaultConnection = (await globalSettingsRepository.LoadAsync(cancellationToken)).Connections
            .FirstOrDefault(item => item is { IsDefault: true, Enabled: true });
        project.SetConnection(
            defaultConnection is null ? null : new ConnectionId(defaultConnection.Id),
            clock.UtcNow);
        var result = await repository.SaveAsync(project, 0, cancellationToken);
        return ToDetails(project, result.Revision);
    }

    public async Task<ProjectUpdateResult> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var projectId = new ProjectId(id);
        var stored = await repository.GetAsync(projectId, cancellationToken);
        if (stored is null)
        {
            return ProjectUpdateResult.NotFound();
        }

        var project = stored.Project;
        project.UpdateDetails(request.Name, request.Description, clock.UtcNow);
        project.SetConnection(
            request.ConnectionId is { } connectionId ? new ConnectionId(connectionId) : null,
            clock.UtcNow);
        var result = await repository.SaveAsync(project, request.Revision, cancellationToken);
        return result.IsSaved
            ? ProjectUpdateResult.Updated(ToDetails(project, result.Revision))
            : ProjectUpdateResult.Conflict(result.Revision);
    }

    public Task<ProjectDeleteResult> DeleteAsync(Guid id, long expectedRevision, CancellationToken cancellationToken) =>
        repository.DeleteAsync(new ProjectId(id), expectedRevision, cancellationToken);





    public async Task<ProjectUpdateResult> UpdateSecurityAsync(
        Guid id,
        UpdateProjectSecurityRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var projectId = new ProjectId(id);
        var stored = await repository.GetAsync(projectId, cancellationToken);
        if (stored is null)
        {
            return ProjectUpdateResult.NotFound();
        }

        var project = stored.Project;
        project.ReplaceSecuritySettings(
            request.DirectoryGrants.Select(item => new DirectoryGrant(
                new DirectoryGrantId(item.Id),
                item.DisplayName,
                item.CanonicalRoot,
                item.Recursive,
                item.ToolNames)),
            request.McpServers.Select(item => new McpServerBinding(
                new McpServerId(item.Id),
                item.DisplayName,
                ParseTransport(item.Transport),
                item.Enabled)),
            request.ToolPolicies.Select(item => new ToolPolicy(
                new ToolIdentity(new McpServerId(item.ServerId), item.Name, item.SchemaHash),
                ParseDecision(item.Decision),
                item.MaxCallsPerRun,
                TimeSpan.FromSeconds(item.TimeoutSeconds))),
            clock.UtcNow);
        var result = await repository.SaveAsync(project, request.Revision, cancellationToken);
        return result.IsSaved
            ? ProjectUpdateResult.Updated(ToDetails(project, result.Revision))
            : ProjectUpdateResult.Conflict(result.Revision);
    }

    private static ProjectSummary ToSummary(Project project, long revision) =>
        new(project.Id.Value, project.Name, project.Description, project.UpdatedAt, revision);

    private static ProjectDetails ToDetails(Project project, long revision) =>
        new(
            project.Id.Value,
            project.Name,
            project.Description,
            project.CreatedAt,
            project.UpdatedAt,
            revision,
            project.DirectoryGrants.Select(item => new DirectoryGrantSettings(
                item.Id.Value,
                item.DisplayName,
                item.CanonicalRoot,
                item.Recursive,
                item.ToolNames.Order(StringComparer.Ordinal).ToArray())).ToArray(),
            project.McpServers.Select(item => new McpServerSettings(
                item.Id.Value,
                item.DisplayName,
                item.Transport.ToString(),
                item.Enabled)).ToArray(),
            project.ToolPolicies.Select(item => new ToolPolicySettings(
                item.Tool.ServerId.Value,
                item.Tool.Name,
                item.Tool.SchemaHash,
                item.Decision.ToString(),
                item.MaxCallsPerRun,
                checked((long)item.Timeout.TotalSeconds))).ToArray(),
            project.ConnectionId?.Value);

    private static McpTransportKind ParseTransport(string value) =>
        Enum.TryParse<McpTransportKind>(value, true, out var transport)
            ? transport
            : throw new ArgumentException($"Unsupported MCP transport '{value}'.", nameof(value));

    private static ToolPolicyDecision ParseDecision(string value) =>
        Enum.TryParse<ToolPolicyDecision>(value, true, out var decision)
            ? decision
            : throw new ArgumentException($"Unsupported tool policy decision '{value}'.", nameof(value));
}
