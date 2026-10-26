namespace AI.Mcp.BuiltIn.Process;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class ProcessStopTool(IProcessSessions processes, IBuiltInToolReply reply) : IToolFactory
{
    public McpServerTool Create() => McpServerTool.Create(StopAsync, new McpServerToolCreateOptions
    {
        Description = "Stop the tree of a processId returned by process_run or process_run_unrestricted in this MCP session. "
            + "Only this session's handles are accepted; never pass an OS PID or stop another chat's process. "
            + "Use only when stopping this run's work is authorized or necessary for the requested task. "
            + "Returns the terminal result and partial logs; stopped is not successful completion and does not undo side effects. "
            + "An already completed process is left unchanged."
    });

    [McpServerTool(Name = "process_stop", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ProcessResult))]
    private async Task<CallToolResult> StopAsync(
        [Description("Opaque processId returned by either launch tool in this MCP session; never an OS PID.")] [MaxLength(64)] string processId,
        CancellationToken cancellationToken = default)
    {
        var result = await processes.StopAsync(processId, cancellationToken);
        // Stopping an owned process is the requested action, not a test pass or an invocation error.
        return reply.Reply(result, result.Status == "failed");
    }
}
