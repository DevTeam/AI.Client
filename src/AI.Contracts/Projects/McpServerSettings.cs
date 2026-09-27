namespace AI.Contracts.Projects;

public sealed record McpServerSettings(Guid Id, string DisplayName, string Transport, bool Enabled);
