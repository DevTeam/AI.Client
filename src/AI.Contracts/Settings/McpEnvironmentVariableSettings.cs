// ReSharper disable NotAccessedPositionalProperty.Global
namespace AI.Contracts.Settings;

public sealed record McpEnvironmentVariableSettings(string Name, string? Value, bool IsSecret, bool HasSecret);
