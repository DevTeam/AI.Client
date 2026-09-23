namespace AI.Client.Domain.Projects;

using Common;

public sealed record ToolPolicy
{
    public ToolPolicy(
        ToolIdentity tool,
        ToolPolicyDecision decision,
        int? maxCallsPerRun = null,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (maxCallsPerRun <= 0)
        {
            throw new DomainException("Tool call limit must be greater than zero.");
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new DomainException("Tool timeout must be greater than zero.");
        }

        Tool = tool;
        Decision = decision;
        MaxCallsPerRun = maxCallsPerRun;
        Timeout = timeout;
    }

    public ToolIdentity Tool { get; }

    public ToolPolicyDecision Decision { get; }

    public int? MaxCallsPerRun { get; }

    public TimeSpan? Timeout { get; }
}
