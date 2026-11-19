namespace AI.Mcp.App;

using System.ComponentModel;
using System.Text.Json;
using AI.Application.Tools;
using AI.Contracts.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

public sealed record CallToolError(string Error);

/// <summary>
/// The stand-in for a tool that discovery found. The agent takes a call to it apart and runs the
/// named tool under that tool's own policy and approval, so the request's tool list stays as it was
/// and the provider can keep reusing its cached start. A call reaches this server only when it could
/// not be taken apart.
/// </summary>
[McpServerToolType]
public sealed class AppCallToolTool : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => new Session(reply).Create();

    private sealed class Session(IAppToolReply reply)
    {
        public McpServerTool Create() => McpServerTool.Create(Call,
            new McpServerToolCreateOptions
            {
                SerializerOptions = reply.Json,
                Description = $"Calls a permitted tool that {ToolRef.ToolSearchName} returned with an inputSchema but that is not in the "
                              + "visible tool list. Pass its exact name and, as arguments, an object that matches its inputSchema. "
                              + "The named tool runs with its own permissions and confirmation, exactly as if it were called directly."
            });

        [McpServerTool(Name = "call_tool", ReadOnly = false, Destructive = true, Idempotent = false,
            OpenWorld = true, UseStructuredContent = true, OutputSchemaType = typeof(CallToolError))]
        private CallToolResult Call(
            [Description("Exact tool name as tool_search returned it.")] string name,
            [Description("Arguments object matching the tool's inputSchema.")] JsonElement? arguments = null) =>
            reply.Reply(new CallToolError(
                $"'{name}' could not be called. Search for it with {ToolRef.ToolSearchName} and pass its exact name "
                + "and an arguments object matching the returned inputSchema."), true);
    }
}
