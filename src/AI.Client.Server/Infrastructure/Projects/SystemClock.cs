namespace AI.Client.Infrastructure.Projects;

using AI.Client.Application.Projects;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
