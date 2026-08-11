using AI.Client.Contracts.Projects;
using AI.Client.Domain.Projects;

namespace AI.Client.Application.Projects;

public sealed class ProjectReader(IProjectRepository repository) : IProjectReader
{
    public async Task<ProjectView?> GetAsync(
        ProjectId id,
        CancellationToken cancellationToken)
    {
        var project = await repository.GetAsync(id, cancellationToken);
        return project is null
            ? null
            : new ProjectView(
                project.Id.Value,
                project.Name,
                project.Description,
                project.CreatedAt,
                project.UpdatedAt);
    }
}
