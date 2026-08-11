namespace AI.Client.Contracts.Projects;

public sealed record ToolPolicySettings(
    Guid ServerId,
    string Name,
    string SchemaHash,
    string Decision,
    int MaxCallsPerRun,
    long TimeoutSeconds);
