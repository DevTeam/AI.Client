using AI.Client.Application.Projects;

namespace AI.Client.Infrastructure.Projects;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
