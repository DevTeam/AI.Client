using AI.Client.Contracts.Projects;
using AI.Client.Domain.Projects;

namespace AI.Client.Application.Projects;

public interface IProjectReader
{
    Task<ProjectView?> GetAsync(ProjectId id, CancellationToken cancellationToken);
}
