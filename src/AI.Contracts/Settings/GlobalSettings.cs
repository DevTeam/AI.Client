namespace AI.Contracts.Settings;

public sealed record GlobalSettings(
    IReadOnlyList<ConnectionSettings> Connections,
    IReadOnlyList<McpServerSettings> McpServers,
    IReadOnlyList<McpToolPolicySettings> ToolPolicies);

public sealed record SaveGlobalSettingsRequest(
    IReadOnlyList<ConnectionSettings> Connections,
    IReadOnlyList<McpServerSettings> McpServers,
    IReadOnlyList<McpToolPolicySettings> ToolPolicies);

public sealed record UpdateSecretRequest(string? Value);
