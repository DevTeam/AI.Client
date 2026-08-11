namespace AI.Client.Application.Projects;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
