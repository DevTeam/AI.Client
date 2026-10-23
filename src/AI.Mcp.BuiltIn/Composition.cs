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
using AI.Sandbox;
using Process;
using Sandbox;
using Scripts;
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
            .Root<ScriptRunner>(nameof(Worker))
            // File system: the platform is one implementation of the contract, a fake in tests is another.
            // The spare script worker is state the whole session shares.
            .Singleton<SystemFileSystem, SystemPath, ScriptWorkers>()
            // Tools
            // Bound to their one contract: registered as a whole they would also expose their shared base.
            .Bind<IToolFactory>(Tag.Unique).To<ProcessRunTool>()
            .Bind<IToolFactory>(Tag.Unique).To<ProcessRunUnrestrictedTool>()
            .Transient<ScriptRunTool, TriggerWaitTool, FetchTool, ListAllowedDirectoriesTool, ReadTextFileTool, ReadMultipleFilesTool, ReadImageFileTool,
                ListDirectoryTool, DirectoryTreeTool,
                SearchFilesTool, GrepFilesTool, GetFileInfoTool, WriteFileTool, EditFileTool, CreateDirectoryTool, MoveFileTool,
                DeleteFileTool, DeleteDirectoryTool, ZipListTool, ZipReadTool, ZipExtractTool, ZipCreateTool>(Tag.Unique)
            .Transient<ProcessRunner, ProcessScriptRunner, ProcessLauncher, ToolSandbox, TriggerWaiter, EnvironmentGrantSource, PathGuard, HtmlText, BuiltInToolReply>()
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
