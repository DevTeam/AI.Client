namespace AI.Client.Mcp.App;

using ModelContextProtocol.Server;

/// <summary>One tool of the application server, built the same way the built-in server builds its own.</summary>
public interface IAppTool
{
    McpServerTool Create();
}
