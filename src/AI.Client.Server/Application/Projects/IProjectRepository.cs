namespace AI.Client.Application.Projects;

using AI.Client.Contracts.Projects;
using AI.Client.Domain.Projects;

public interface IProjectRepository
{
    Task<StoredProject?> GetAsync(ProjectId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredProject>> ListAsync(CancellationToken cancellationToken);

    Task<ProjectSaveResult> SaveAsync(
        Project project,
        long expectedRevision,
        CancellationToken cancellationToken);

    Task<ProjectDeleteResult> DeleteAsync(
        ProjectId id,
        long expectedRevision,
        CancellationToken cancellationToken);
}
