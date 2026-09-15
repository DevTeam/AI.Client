namespace AI.Client.Contracts.Settings;

/// <summary>
/// The Host's own MCP server, which exposes the application's data — projects, chats, runs and
/// settings — as tools. It speaks the same protocol as any other server, but its transport is a
/// pair of in-process pipes rather than a child process, so it has no executable to configure.
/// </summary>
public static class AppMcpServer
{
    public static readonly Guid Id = new("9f3d6c41-5a2e-4d0b-93f7-2c8a6e1b4d55");

    /// <summary>Transport name reserved for servers the Host hosts itself.</summary>
    public const string Transport = "InProcess";

    public static McpServerSettings Settings => new(Id, "App tools", Transport, true, "Ask", null,
        null, [], null, [], false);
}
