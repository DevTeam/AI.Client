namespace AI.Mcp.BuiltIn.Triggers;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class TriggerWaitTool(ITriggerWaiter waiter, IBuiltInToolReply reply) : IToolFactory
{
    public McpServerTool Create() => McpServerTool.Create(
        WaitAsync,
        new McpServerToolCreateOptions
        {
            Description = "Wait once for the first file, process or time condition. The wait ends on a match, timeout or chat cancellation; it does not survive the current run. "
                          + "Types: delay (afterMs), file_exists/file_missing/file_changed (absolute path), process_exit, "
                          + "process_cpu_below/process_cpu_above (processId and cpuPercent), process_memory_below/process_memory_above "
                          + "(processId and memoryBytes). CPU percent is normalized across logical processors. "
                          + "stableForMs requires a file or metric condition to hold without change. "
                          + "File paths need a read grant. Process metrics reveal only the named PID, not its command line or environment."
        });

    [McpServerTool(Name = "trigger_wait", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = true,
        UseStructuredContent = true, OutputSchemaType = typeof(TriggerWaitResult))]
    private async Task<CallToolResult> WaitAsync(
        [Description("One to eight conditions; the first satisfied condition wins.")] TriggerCondition[] conditions,
        [Description("Maximum wait in milliseconds, 1 to 86400000; defaults to 600000. The effective tool policy may shorten it.")]
        [Range(1, TriggerWaiter.MaxTimeoutMs)] int timeoutMs = 600000,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await waiter.WaitAsync(conditions, timeoutMs, cancellationToken);
            return reply.Reply(result, isError: result.Error is not null);
        }
        catch (Exception error) when (error is ArgumentException or GrantException or IOException or UnauthorizedAccessException
                                      or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return reply.Reply(new TriggerWaitResult("error", null, null, null, null, null, null, 0, false, error.Message), true);
        }
    }
}
