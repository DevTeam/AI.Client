namespace AI.Client.Mcp.App;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

/// <summary>Builds the application's own MCP server over a transport the caller supplies.</summary>
public interface IAppMcpServerHost
{
    McpServer Create(ITransport transport);
}
