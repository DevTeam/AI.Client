namespace AI.Client.Infrastructure.Settings;

public sealed class GlobalSettingsPaths(string rootDirectory)
{
    public string ConnectionsPath { get; } = Path.Combine(rootDirectory, "connections.json");

    public string McpServersPath { get; } = Path.Combine(rootDirectory, "mcp-servers.json");

    public string GetSecretPath(string scope, Guid id) =>
        Path.Combine(rootDirectory, "credentials", $"{scope}-{id:N}.protected");
}
