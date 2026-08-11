using AI.Client.Contracts.Projects;
using AI.Client.Application.Settings;
using AI.Client.Domain.Projects;

namespace AI.Client.Application.Projects;

public sealed class ProjectService(
    IProjectRepository repository,
    IProjectIdGenerator idGenerator,
    IClock clock,
    IEndpointCredentialStore credentialStore,
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
        var project = await repository.GetAsync(projectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var stored = (await repository.ListAsync(cancellationToken))
            .Single(item => item.Project.Id == projectId);
        return await ToDetailsAsync(stored.Project, stored.Revision, cancellationToken);
    }

    public async Task<ProjectDetails> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var project = new Project(idGenerator.Create(), request.Name, request.Description, clock.UtcNow);
        var defaultConnection = (await globalSettingsRepository.LoadAsync(cancellationToken)).Connections
            .FirstOrDefault(item => item.IsDefault && item.Enabled);
        project.SetConnection(
            defaultConnection is null ? null : new EndpointProfileId(defaultConnection.Id),
            clock.UtcNow);
        var result = await repository.SaveAsync(project, 0, cancellationToken);
        return await ToDetailsAsync(project, result.Revision, cancellationToken);
    }

    public async Task<ProjectUpdateResult> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var projectId = new ProjectId(id);
        var project = await repository.GetAsync(projectId, cancellationToken);
        if (project is null)
        {
            return ProjectUpdateResult.NotFound();
        }

        project.UpdateDetails(request.Name, request.Description, clock.UtcNow);
        project.SetConnection(
            request.ConnectionId is { } connectionId ? new EndpointProfileId(connectionId) : null,
            clock.UtcNow);
        var result = await repository.SaveAsync(project, request.Revision, cancellationToken);
        return result.IsSaved
            ? ProjectUpdateResult.Updated(await ToDetailsAsync(project, result.Revision, cancellationToken))
            : ProjectUpdateResult.Conflict(result.Revision);
    }

    public Task<ProjectDeleteResult> DeleteAsync(Guid id, long expectedRevision, CancellationToken cancellationToken) =>
        repository.DeleteAsync(new ProjectId(id), expectedRevision, cancellationToken);

    public async Task<ProjectUpdateResult> UpdateEndpointProfilesAsync(
        Guid id,
        UpdateEndpointProfilesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var project = await repository.GetAsync(new ProjectId(id), cancellationToken);
        if (project is null)
        {
            return ProjectUpdateResult.NotFound();
        }

        var removedProfileIds = project.EndpointProfiles
            .Select(item => item.Id)
            .Except(request.Profiles.Select(item => new EndpointProfileId(item.Id)))
            .ToArray();
        project.ReplaceEndpointProfiles(request.Profiles.Select(item => new EndpointProfile(
            new EndpointProfileId(item.Id), item.Name, item.BaseUrl, item.Model)), clock.UtcNow);
        project.SetDefaultEndpointProfile(
            request.DefaultEndpointProfileId is { } defaultId ? new EndpointProfileId(defaultId) : null,
            clock.UtcNow);
        var result = await repository.SaveAsync(project, request.Revision, cancellationToken);
        if (!result.IsSaved)
        {
            return ProjectUpdateResult.Conflict(result.Revision);
        }

        foreach (var profileId in removedProfileIds)
        {
            await credentialStore.SetAsync(profileId, null, cancellationToken);
        }

        return ProjectUpdateResult.Updated(await ToDetailsAsync(project, result.Revision, cancellationToken));
    }

    public async Task<bool> SetEndpointCredentialAsync(
        Guid projectId,
        Guid endpointProfileId,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        var project = await repository.GetAsync(new ProjectId(projectId), cancellationToken);
        var profileId = new EndpointProfileId(endpointProfileId);
        if (project is null || !project.EndpointProfiles.Any(item => item.Id == profileId))
        {
            return false;
        }

        await credentialStore.SetAsync(profileId, apiKey, cancellationToken);
        return true;
    }

    public async Task<ProjectSecurityUpdateResult> UpdateSecurityAsync(
        Guid id,
        UpdateProjectSecurityRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var projectId = new ProjectId(id);
        var project = await repository.GetAsync(projectId, cancellationToken);
        if (project is null)
        {
            return ProjectSecurityUpdateResult.NotFound();
        }

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
            ? ProjectSecurityUpdateResult.Updated(await ToDetailsAsync(project, result.Revision, cancellationToken))
            : ProjectSecurityUpdateResult.Conflict(result.Revision);
    }

    private static ProjectSummary ToSummary(Project project, long revision) =>
        new(project.Id.Value, project.Name, project.Description, project.UpdatedAt, revision);

    private async Task<ProjectDetails> ToDetailsAsync(Project project, long revision, CancellationToken cancellationToken) =>
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
            await Task.WhenAll(project.EndpointProfiles.Select(async item => new EndpointProfileSettings(
                item.Id.Value,
                item.Name,
                item.BaseUrl,
                item.Model,
                await credentialStore.ExistsAsync(item.Id, cancellationToken)))),
            project.DefaultEndpointProfileId?.Value,
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
