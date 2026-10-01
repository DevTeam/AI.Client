using ModelContextProtocol.Server;

namespace AI.Mcp.CSharp;

public interface IToolFactory
{
    McpServerTool Create();
}
