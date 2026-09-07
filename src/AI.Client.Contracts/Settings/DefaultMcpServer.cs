namespace AI.Client.Contracts.Settings;

public static class DefaultMcpServer
{
    public static readonly Guid Id = new("6626cd30-f2ad-4cd4-a754-f3f41f814ab8");
    public static McpServerSettings Settings => new(Id, "Default tools", "Stdio", true, "Ask", null,
        null, [], null, [], false);
}
