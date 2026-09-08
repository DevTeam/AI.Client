// ReSharper disable UnusedMember.Local

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AI.Client.Mcp.BuiltIn;

using Pure.DI;
using System.Diagnostics;

internal sealed partial class Composition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .Hint(Hint.Resolve, "Off")
            .Hint(Hint.ThreadSafe, "Off")
            .Root<McpServer>(nameof(Server))
            .Singleton<ProcessRunner>()
            .Transient<ProcessRunTool>(Tag.Unique)
            .Transient((IEnumerable<IToolFactory> toolFactories) =>
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
