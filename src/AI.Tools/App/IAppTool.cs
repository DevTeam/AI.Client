namespace AI.Mcp.App;

using AI.Application.Tools;
using ModelContextProtocol.Server;

/// <summary>
/// One tool of the application server, built the same way the built-in server builds its own.
/// It is built per session, so the run it will serve is a parameter rather than something to look
/// up: most tools ignore it, and the one that has to reach the person cannot work without it.
/// </summary>
public interface IAppTool
{
    McpServerTool Create(ToolRunContext run, IAppToolReply reply);
}
