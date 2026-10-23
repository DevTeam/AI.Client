namespace AI.Contracts.Settings;

public sealed record GlobalSettings(
    IReadOnlyList<ConnectionSettings> Connections,
    IReadOnlyList<McpServerSettings> McpServers,
    IReadOnlyList<McpToolPolicySettings> ToolPolicies,
    // Absent in documents written before these switches existed, which means both are on.
    // A "sandbox" member some documents carry from a short-lived setting is ignored on read.
    ChatAutomationSettings? ChatAutomation = null);

public sealed record SaveGlobalSettingsRequest(
    IReadOnlyList<ConnectionSettings> Connections,
    IReadOnlyList<McpServerSettings> McpServers,
    IReadOnlyList<McpToolPolicySettings> ToolPolicies);

public sealed record UpdateSecretRequest(string? Value);

public sealed record SettingsItemChange<T>(T Item, T? Expected);

public sealed record SettingsItemRemoval<T>(T Expected);
