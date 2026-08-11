using AI.Client.Domain.Projects;

namespace AI.Client.Application.Projects;

public interface IProjectIdGenerator
{
    ProjectId Create();
}
