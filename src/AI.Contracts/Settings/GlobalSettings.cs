namespace AI.Contracts.Settings;

public sealed record GlobalSettings(
    IReadOnlyList<ConnectionSettings> Connections,
    IReadOnlyList<McpServerSettings> McpServers,
    IReadOnlyList<McpToolPolicySettings> ToolPolicies,
    // Absent in documents written before these switches existed, which means both are on.
    ChatAutomationSettings? ChatAutomation = null);

public sealed record SaveGlobalSettingsRequest(
    IReadOnlyList<ConnectionSettings> Connections,
    IReadOnlyList<McpServerSettings> McpServers,
    IReadOnlyList<McpToolPolicySettings> ToolPolicies);

public sealed record UpdateSecretRequest(string? Value);
