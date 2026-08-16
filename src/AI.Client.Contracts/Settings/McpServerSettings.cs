// ReSharper disable NotAccessedPositionalProperty.Global

namespace AI.Client.Contracts.Settings;

public sealed record McpServerSettings(
    Guid Id,
    string Name,
    string Transport,
    bool Enabled,
    string Policy,
    string? Url,
    string? Command,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    IReadOnlyList<McpEnvironmentVariableSettings> EnvironmentVariables,
    bool HasCredential);
