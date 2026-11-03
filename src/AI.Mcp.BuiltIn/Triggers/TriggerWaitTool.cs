namespace AI.Mcp.BuiltIn.Triggers;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Grants;
using ModelContextProtocol;
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
                          + "Types: delay (afterMs), file_exists/file_missing/file_changed (absolute path), file_contains (path and pattern, optional regex/ignoreCase), "
                          + "file_idle (path and quietMs, the time the file must stay unchanged), directory_idle (path and quietMs, the time every entry directly inside the directory must stay unchanged), process_exit, "
                          + "process_completed (processSessionId from process_run or process_run_sandboxed, optional stdoutOffset/stderrOffset; returns processResult with status, exitCode and new output), "
                          + "process_output_contains (processSessionId and pattern, optional regex/ignoreCase), "
                          + "process_cpu_below/process_cpu_above (OS processId and cpuPercent), process_memory_below/process_memory_above "
                          + "(processId and memoryBytes). Each condition takes only its own fields and rejects the others, so a "
                          + "misnamed field fails instead of being ignored. "
                          + "stableForMs (0 to 86400000) holds a file or metric condition true before it fires; process_exit and "
                          + "process_completed do not take it. CPU percent is normalized across logical processors. "
                          + "Text conditions read a file's last 256 KiB, or the output a managed process printed after the given offsets, "
                          + "and a literal pattern is a plain substring while regex enables a regular expression. "
                          + "A timeout returns the state it last read for every condition in observed, so a missing file is "
                          + "distinguishable from an unparsed process or a pattern that was never printed. "
                          + "Prefer process_completed for one bounded wait on a managed command; it also matches commands already finished. Timeout/cancellation does not stop the command or change its hard deadline. "
                          + "Managed handles belong to this MCP session, not OS PIDs. File paths need a read grant. Process metrics reveal only the named PID, not its command line or environment."
        });

    [McpServerTool(Name = "trigger_wait", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = true,
        UseStructuredContent = true, OutputSchemaType = typeof(TriggerWaitResult))]
    private async Task<CallToolResult> WaitAsync(
        [Description("One to eight conditions; the first satisfied condition wins.")]
        [MinLength(1)] [MaxLength(TriggerWaiter.MaxConditions)] TriggerCondition[] conditions,
        IProgress<ProgressNotificationValue> progress,
        [Description("Maximum wait in milliseconds, 1 to 86400000; defaults to 600000. The effective tool policy may shorten it.")]
        [Range(1, TriggerWaiter.MaxTimeoutMs)] int timeoutMs = 600000,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await waiter.WaitAsync(conditions, timeoutMs, new ForwardProgress(progress), cancellationToken);
            return reply.Reply(result, isError: result.Error is not null);
        }
        catch (Exception error) when (error is ArgumentException or GrantException or IOException or UnauthorizedAccessException
                                      or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return reply.Reply(new TriggerWaitResult("error", null, null, null, null, null, null, 0, false,
                error.Message, TimeoutMs: timeoutMs), true);
        }
    }

    /// <summary>
    /// Adapts the waiter's own report to the protocol's progress notification. The elapsed time is
    /// the value and the wait's limit the total, so a card can show how much of the wait is left;
    /// the message names what is being waited for.
    /// </summary>
    private sealed class ForwardProgress(IProgress<ProgressNotificationValue> sink) : IProgress<TriggerProgress>
    {
        public void Report(TriggerProgress value) => sink.Report(new ProgressNotificationValue
            { Progress = value.ElapsedMs, Total = value.TimeoutMs, Message = value.Message });
    }
}
