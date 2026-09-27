using ModelContextProtocol.Server;

namespace AI.Mcp.BuiltIn;

public interface IToolFactory
{
    McpServerTool Create();
}