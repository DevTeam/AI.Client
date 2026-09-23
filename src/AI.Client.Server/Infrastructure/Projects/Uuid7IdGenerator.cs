namespace AI.Client.Infrastructure.Projects;

using AI.Client.Application.Projects;

public sealed class Uuid7IdGenerator : IIdGenerator
{
    public Guid Create() => Guid.CreateVersion7();
}
