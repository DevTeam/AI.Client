namespace AI.Application.Projects;

using AI.Contracts.Projects;

public interface IProjectService
{
    Task<IReadOnlyList<ProjectSummary>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Remembers the order the person gave the projects; <see cref="ListAsync"/> follows it.</summary>
    Task ReorderAsync(ReorderProjectsRequest request, CancellationToken cancellationToken);

    Task<ProjectDetails?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<ProjectDetails> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken);

    Task<ProjectUpdateResult> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken cancellationToken);

    Task<ProjectUpdateResult> UpdateSecurityAsync(
        Guid id,
        UpdateProjectSecurityRequest request,
        CancellationToken cancellationToken);

    Task<ProjectUpdateResult> AddDirectoryGrantAsync(
        Guid id, long expectedRevision, DirectoryGrantSettings grant, CancellationToken cancellationToken);

    Task<ProjectUpdateResult> AddDirectoryGrantsAsync(
        Guid id, long expectedRevision, IReadOnlyList<DirectoryGrantSettings> grants, CancellationToken cancellationToken);

    Task<ProjectUpdateResult> RemoveDirectoryGrantAsync(
        Guid id, long expectedRevision, Guid grantId, CancellationToken cancellationToken);

    Task<ProjectDetails?> SetToolPolicyAsync(Guid id, ToolPolicySettings policy, CancellationToken cancellationToken);
    Task<ProjectDetails?> RemoveToolPolicyAsync(Guid id, Guid serverId, string name, string schemaHash, CancellationToken cancellationToken);



    Task<ProjectDeleteResult> DeleteAsync(Guid id, long expectedRevision, CancellationToken cancellationToken);
}
