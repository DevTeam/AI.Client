using AI.Client.Contracts.Projects;

namespace AI.Client.Application.Projects;

public interface IProjectService
{
    Task<IReadOnlyList<ProjectSummary>> ListAsync(CancellationToken cancellationToken);

    Task<ProjectDetails?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<ProjectDetails> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken);

    Task<ProjectUpdateResult> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken cancellationToken);

    Task<ProjectSecurityUpdateResult> UpdateSecurityAsync(
        Guid id,
        UpdateProjectSecurityRequest request,
        CancellationToken cancellationToken);

    Task<ProjectUpdateResult> UpdateEndpointProfilesAsync(
        Guid id,
        UpdateEndpointProfilesRequest request,
        CancellationToken cancellationToken);

    Task<bool> SetEndpointCredentialAsync(
        Guid projectId,
        Guid endpointProfileId,
        string? apiKey,
        CancellationToken cancellationToken);

    Task<ProjectDeleteResult> DeleteAsync(Guid id, long expectedRevision, CancellationToken cancellationToken);
}
