namespace AI.Contracts.Settings;

/// <summary>Tests the configuration currently entered in the editor without saving it.</summary>
public sealed record DiscoverMcpToolsRequest(McpServerSettings Server, string? Credential = null);
