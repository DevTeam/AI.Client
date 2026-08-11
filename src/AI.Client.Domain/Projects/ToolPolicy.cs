using AI.Client.Domain.Common;

namespace AI.Client.Domain.Projects;

public sealed record ToolPolicy
{
    public ToolPolicy(
        ToolIdentity tool,
        ToolPolicyDecision decision,
        int maxCallsPerRun = 20,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (maxCallsPerRun <= 0)
        {
            throw new DomainException("Tool call limit must be greater than zero.");
        }

        var actualTimeout = timeout ?? TimeSpan.FromMinutes(2);
        if (actualTimeout <= TimeSpan.Zero)
        {
            throw new DomainException("Tool timeout must be greater than zero.");
        }

        Tool = tool;
        Decision = decision;
        MaxCallsPerRun = maxCallsPerRun;
        Timeout = actualTimeout;
    }

    public ToolIdentity Tool { get; }

    public ToolPolicyDecision Decision { get; }

    public int MaxCallsPerRun { get; }

    public TimeSpan Timeout { get; }
}
