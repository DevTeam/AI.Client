// ReSharper disable UnusedMember.Local
namespace AI.Client.Mcp.BuiltIn;

using System.Diagnostics;
using Files;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Process;
using Pure.DI;
using Web;

internal sealed partial class Composition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .Hint(Hint.Resolve, "Off")
            .Hint(Hint.ThreadSafe, "Off")
            .Root<Program>(nameof(Root))
            // Tools
            .Transient<ProcessRunTool, FetchTool, ListAllowedDirectoriesTool, ReadTextFileTool, ReadMultipleFilesTool, ListDirectoryTool, DirectoryTreeTool,
                SearchFilesTool, GrepFilesTool, GetFileInfoTool, WriteFileTool, EditFileTool, CreateDirectoryTool, MoveFileTool>(Tag.Unique)
            .Singleton<ProcessRunner, EnvironmentGrantSource, PathGuard, WebFetcher, HtmlText>()
            .Singleton((IEnumerable<IToolFactory> toolFactories) =>
            {
                var tools = new McpServerPrimitiveCollection<McpServerTool>();
                foreach (var toolFactory in toolFactories)
                {
                    tools.Add(toolFactory.Create());
                }

                return tools;
            })
            .Transient((McpServerPrimitiveCollection<McpServerTool> tools) => McpServer.Create(
                new StdioServerTransport("Built‑in"),
                new McpServerOptions
                {
                    ServerInfo = new Implementation { Name = "Built‑in", Version = "1.0.0" },
                    ToolCollection = tools
                }));
}
