using ModelContextProtocol.Server;

namespace AI.Client.Mcp.BuiltIn;

public interface IToolFactory
{
    McpServerTool Create();
}