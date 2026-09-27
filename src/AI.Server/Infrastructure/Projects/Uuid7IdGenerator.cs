namespace AI.Infrastructure.Projects;

using AI.Application.Projects;

public sealed class Uuid7IdGenerator : IIdGenerator
{
    public Guid Create() => Guid.CreateVersion7();
}
