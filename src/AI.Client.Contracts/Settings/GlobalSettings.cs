namespace AI.Client.Contracts.Settings;

public sealed record GlobalSettings(
    IReadOnlyList<ConnectionSettings> Connections,
    IReadOnlyList<McpServerSettings> McpServers);

public sealed record SaveGlobalSettingsRequest(
    IReadOnlyList<ConnectionSettings> Connections,
    IReadOnlyList<McpServerSettings> McpServers);

public sealed record UpdateSecretRequest(string? Value);
