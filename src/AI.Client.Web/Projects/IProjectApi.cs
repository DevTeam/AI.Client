using AI.Client.Contracts.Projects;

namespace AI.Client.Web.Projects;

public interface IProjectApi
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
        Guid profileId,
        UpdateEndpointCredentialRequest request,
        CancellationToken cancellationToken);

    Task<ProjectDeleteResult> DeleteAsync(Guid id, long revision, CancellationToken cancellationToken);
}
