// ReSharper disable UnusedMember.Local
namespace AI.Mcp.CSharp;

using System.Diagnostics;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Pure.DI;
using Scripts;

internal sealed partial class Composition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .Hint(Hint.Resolve, "Off")
            .Hint(Hint.ThreadSafe, "Off")
            .Root<Program>(nameof(Root))
            // Tools
            .Transient<ScriptRunTool>(Tag.Unique)
            .Singleton<ScriptRunner, ToolReply>()
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
                new StdioServerTransport("C# scripts"),
                new McpServerOptions
                {
                    ServerInfo = new Implementation { Name = "C# scripts", Version = "1.0.0" },
                    ToolCollection = tools
                }));
}
