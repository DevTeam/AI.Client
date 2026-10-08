namespace AI.Contracts.Settings;

public sealed record McpToolPolicySettings(
    Guid ServerId,
    string Name,
    string SchemaHash,
    string Decision,
    int MaxCallsPerRun,
    long TimeoutSeconds)
{
    public const int DefaultMaxCallsPerRun = 56535;
    public const int DefaultTimeoutSeconds = 600;
    public const int MaxTimeoutSeconds = 3600;
}
