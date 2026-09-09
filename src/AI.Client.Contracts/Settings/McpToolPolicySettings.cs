namespace AI.Client.Contracts.Settings;

public sealed record McpToolPolicySettings(
    Guid ServerId,
    string Name,
    string SchemaHash,
    string Decision,
    int MaxCallsPerRun,
    long TimeoutSeconds);
