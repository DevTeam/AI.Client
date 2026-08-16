namespace AI.Client.Application.Projects;

using AI.Client.Contracts.Projects;

public interface IProjectService
{
    Task<IReadOnlyList<ProjectSummary>> ListAsync(CancellationToken cancellationToken);

    Task<ProjectDetails?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<ProjectDetails> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken);

    Task<ProjectUpdateResult> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken cancellationToken);

    Task<ProjectUpdateResult> UpdateSecurityAsync(
        Guid id,
        UpdateProjectSecurityRequest request,
        CancellationToken cancellationToken);



    Task<ProjectDeleteResult> DeleteAsync(Guid id, long expectedRevision, CancellationToken cancellationToken);
}
