// ReSharper disable UseCollectionExpression
namespace AI.Application.Projects;

using Settings;
using AI.Contracts.Projects;
using AI.Domain.Projects;

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
        // New projects start with no explicit connection so new chats fall back to the global
        // default. Pinning one here would force the user to overwrite the field the moment they
        // want to follow the global default.
        project.SetConnection(null, clock.UtcNow);
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
        // 'UseDefaultConnection' is the explicit way to clear an explicit choice; a null
        // 'ConnectionId' on its own would still mean "leave the stored value untouched" so
        // callers editing only the name do not accidentally flip the project onto the global
        // default.
        // The explicit type is needed: both branches can be null, so 'var' would refuse to
        // resolve the type of the conditional. The setter accepts a nullable id.
        ConnectionId? newConnection = request.UseDefaultConnection
            ? null
            : request.ConnectionId is { } connectionId ? new ConnectionId(connectionId) : null;
        project.SetConnection(newConnection, clock.UtcNow);
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
                item.TimeoutSeconds is { } timeout ? TimeSpan.FromSeconds(timeout) : null)),
            clock.UtcNow);
        var result = await repository.SaveAsync(project, request.Revision, cancellationToken);
        return result.IsSaved
            ? ProjectUpdateResult.Updated(ToDetails(project, result.Revision))
            : ProjectUpdateResult.Conflict(result.Revision);
    }

    public async Task<ProjectUpdateResult> AddDirectoryGrantAsync(
        Guid id, long expectedRevision, DirectoryGrantSettings grant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grant);
        var stored = await repository.GetAsync(new ProjectId(id), cancellationToken);
        if (stored is null) return ProjectUpdateResult.NotFound();

        var project = stored.Project;
        project.AddDirectoryGrant(new DirectoryGrant(
            new DirectoryGrantId(grant.Id), grant.DisplayName, grant.CanonicalRoot,
            grant.Recursive, grant.ToolNames), clock.UtcNow);
        var result = await repository.SaveAsync(project, expectedRevision, cancellationToken);
        return result.IsSaved
            ? ProjectUpdateResult.Updated(ToDetails(project, result.Revision))
            : ProjectUpdateResult.Conflict(result.Revision);
    }

    public async Task<ProjectUpdateResult> RemoveDirectoryGrantAsync(
        Guid id, long expectedRevision, Guid grantId, CancellationToken cancellationToken)
    {
        var stored = await repository.GetAsync(new ProjectId(id), cancellationToken);
        if (stored is null) return ProjectUpdateResult.NotFound();

        var project = stored.Project;
        project.RemoveDirectoryGrant(new DirectoryGrantId(grantId), clock.UtcNow);
        var result = await repository.SaveAsync(project, expectedRevision, cancellationToken);
        return result.IsSaved
            ? ProjectUpdateResult.Updated(ToDetails(project, result.Revision))
            : ProjectUpdateResult.Conflict(result.Revision);
    }

    public async Task<ProjectDetails?> SetToolPolicyAsync(Guid id, ToolPolicySettings policy, CancellationToken cancellationToken)
    {
        var stored = await repository.GetAsync(new ProjectId(id), cancellationToken);
        if (stored is null) return null;
        if (stored.Project.McpServers.All(server => server.Id.Value != policy.ServerId))
        {
            var server = (await globalSettingsRepository.LoadAsync(cancellationToken)).McpServers
                .SingleOrDefault(item => item.Id == policy.ServerId) ?? throw new ArgumentException("MCP server not found.");
            stored.Project.AddMcpServer(new McpServerBinding(new McpServerId(server.Id), server.Name,
                ParseTransport(server.Transport), server.Enabled), clock.UtcNow);
        }
        stored.Project.SetToolPolicy(new ToolPolicy(new ToolIdentity(new McpServerId(policy.ServerId), policy.Name,
            policy.SchemaHash), ParseDecision(policy.Decision), policy.MaxCallsPerRun,
            policy.TimeoutSeconds is { } timeout ? TimeSpan.FromSeconds(timeout) : null), clock.UtcNow);
        var result = await repository.SaveAsync(stored.Project, stored.Revision, cancellationToken);
        return result.IsSaved ? ToDetails(stored.Project, result.Revision) : null;
    }

    public async Task<ProjectDetails?> RemoveToolPolicyAsync(Guid id, Guid serverId, string name,
        string schemaHash, CancellationToken cancellationToken)
    {
        var stored = await repository.GetAsync(new ProjectId(id), cancellationToken);
        if (stored is null) return null;
        stored.Project.RemoveToolPolicy(new ToolIdentity(new McpServerId(serverId), name, schemaHash), clock.UtcNow);
        var result = await repository.SaveAsync(stored.Project, stored.Revision, cancellationToken);
        return result.IsSaved ? ToDetails(stored.Project, result.Revision) : null;
    }

    private static ProjectSummary ToSummary(Project project, long revision) =>
        new(project.Id.Value, project.Name, project.Description, project.UpdatedAt, revision, project.ConnectionId?.Value);

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
                item.Timeout is { } timeout ? checked((long)timeout.TotalSeconds) : null)).ToArray(),
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
