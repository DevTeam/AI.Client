namespace AI.Mcp.BuiltIn.Process;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class ProcessWaitTool(IProcessSessions processes, IBuiltInToolReply reply) : IToolFactory
{
    public McpServerTool Create() => McpServerTool.Create(WaitAsync, new McpServerToolCreateOptions
    {
        Description = "Wait for a processId returned by process_run or process_run_unrestricted in this MCP session. "
            + "This never launches a command or changes its sandbox, permissions or hard deadline. "
            + "A wait expiring returns status running, not an error; wait again instead of restarting or splitting tests. "
            + "Pass the previous stdoutOffset/stderrOffset to read only new output, or 0 to replay the logs. "
            + "Offsets count original bytes. Each stream returns at most 32768 bytes; hasMoreOutput means unread output remains. "
            + "A completed process can still have unread output: use waitTimeoutMs 0 to page it. "
            + "Full byte logs remain at stdoutPath/stderrPath. Only completed with exitCode 0 establishes success. "
            + "Cancelling this wait leaves the process running; process_stop explicitly stops its tree."
    });

    [McpServerTool(Name = "process_wait", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ProcessResult))]
    private async Task<CallToolResult> WaitAsync(
        [Description("Opaque processId returned by either process launch tool in this MCP session; never an OS PID.")] [MaxLength(64)] string processId,
        [Description("Maximum wait in milliseconds, 0 for a status/output snapshot. Expiry does not stop the process.")] [Range(0, ProcessSessions.MaxWaitTimeoutMs)] int waitTimeoutMs = ProcessSessions.MaxWaitTimeoutMs,
        [Description("stdoutOffset from the previous result, in bytes; 0 replays stdout from its start.")] [Range(0, long.MaxValue)] long stdoutOffset = 0,
        [Description("stderrOffset from the previous result, in bytes; 0 replays stderr from its start.")] [Range(0, long.MaxValue)] long stderrOffset = 0,
        CancellationToken cancellationToken = default)
    {
        ProcessResult result;
        try { result = await processes.WaitAsync(processId, waitTimeoutMs, stdoutOffset, stderrOffset, cancellationToken); }
        catch (ArgumentException error) { result = new ProcessResult(null, "", "", 0, false, false, error.Message, Status: "failed"); }
        return reply.Reply(result, result.Status != "running" && (result.Error is not null || result.TimedOut || result.ExitCode != 0));
    }
}
