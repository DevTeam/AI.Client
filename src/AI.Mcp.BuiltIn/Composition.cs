// DI guide: [Pure.DI conventions](../../docs/30-dependency-injection.md).
// ReSharper disable UnusedMember.Local
namespace AI.Mcp.BuiltIn;

using System.Diagnostics;
using AI.Contracts.FileSystem;
using Archives;
using Files;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Process;
using Triggers;
using Pure.DI;
using Web;

internal sealed partial class Composition
{
    [Conditional("DI")]
    private static void Setup() =>
        DI.Setup()
            .Hint(Hint.Comments, "Off")
            .Hint(Hint.Resolve, "Off")
            .Hint(Hint.ThreadSafe, "Off")
            .Root<Program>(nameof(Root))
            // Tools
            .Transient<ProcessRunTool, TriggerWaitTool, FetchTool, ListAllowedDirectoriesTool, ReadTextFileTool, ReadMultipleFilesTool, ReadImageFileTool,
                ListDirectoryTool, DirectoryTreeTool,
                SearchFilesTool, GrepFilesTool, GetFileInfoTool, WriteFileTool, EditFileTool, CreateDirectoryTool, MoveFileTool,
                DeleteFileTool, DeleteDirectoryTool, ZipListTool, ZipReadTool, ZipExtractTool, ZipCreateTool>(Tag.Unique)
            .Transient<ProcessRunner, TriggerWaiter, EnvironmentGrantSource, PathGuard, HtmlText, BuiltInToolReply>()
            // File system: the platform is one implementation of the contract, a fake in tests is another.
            .Singleton<SystemFileSystem, SystemPath>()
            .Transient(_ => new WebFetcher(WebFetcher.CreateDefaultHandler()))
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
