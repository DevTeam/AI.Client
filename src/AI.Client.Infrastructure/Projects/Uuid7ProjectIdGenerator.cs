using AI.Client.Application.Projects;
using AI.Client.Domain.Projects;

namespace AI.Client.Infrastructure.Projects;

public sealed class Uuid7ProjectIdGenerator : IProjectIdGenerator
{
    public ProjectId Create() => new(Guid.CreateVersion7());
}
