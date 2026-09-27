namespace AI.Infrastructure.Projects;

using AI.Application.Projects;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
