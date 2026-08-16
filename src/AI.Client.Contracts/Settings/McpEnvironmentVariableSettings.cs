// ReSharper disable NotAccessedPositionalProperty.Global
namespace AI.Client.Contracts.Settings;

public sealed record McpEnvironmentVariableSettings(string Name, string? Value, bool IsSecret, bool HasSecret);
