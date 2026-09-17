namespace AI.Client.Mcp.App;

using AI.Client.Application.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

/// <summary>
/// Builds the application's MCP server. It is an ordinary server in every respect the protocol
/// cares about — the same <c>tools/list</c>, the same schemas, the same results — and differs only
/// in its transport, which the caller supplies. Running it inside the Host is what gives it access
/// to the application services; nothing about the protocol is shortcut to achieve that.
/// </summary>
public sealed class AppMcpServerHost(IEnumerable<IAppTool> tools) : IAppMcpServerHost
{
    public const string Name = "App tools";

    public McpServer Create(ITransport transport, ToolRunContext run)
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        foreach (var tool in tools) collection.Add(tool.Create(run));
        return McpServer.Create(transport, new McpServerOptions
        {
            ServerInfo = new Implementation { Name = Name, Version = "1.0.0" },
            ServerInstructions = "These tools read and change the data of the AI client you are running inside: its projects, chats, "
                                 + "branches, message queues and settings. Read before you write, pass the revision you read, and use a "
                                 + "fresh operationId for each distinct change. When a job would fill your context with detail you do not need to keep, "
                                 + "delegate it with spawn_subtask and keep only the answer. When a choice is genuinely the user's to make and "
                                 + "guessing wrong would waste real work, ask them with ask_user instead of guessing.",
            ToolCollection = collection
        });
    }
}
