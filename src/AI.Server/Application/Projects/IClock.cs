namespace AI.Application.Projects;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
