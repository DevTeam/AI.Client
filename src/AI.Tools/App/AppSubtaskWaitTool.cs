namespace AI.Mcp.App;

using ModelContextProtocol;
using System.ComponentModel.DataAnnotations;
using AI.Application.Tools;
using AI.Contracts.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class AppSubtaskWaitTool(ISubtaskRuns tasks) : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => new Session(tasks, run, reply).Create();

    private sealed class Session(ISubtaskRuns tasks, ToolRunContext run, IAppToolReply reply)
    {
        public McpServerTool Create() => McpServerTool.Create(WaitAsync, new McpServerToolCreateOptions
        {
            SerializerOptions = reply.Json,
            Description = "Collect run-owned subtask handles without restarting work. any waits for the first finished task; "
                + "all waits for the whole selection. Returns each task's status and terminal result, including checks, artifacts and "
                + "unresolved items. waitTimeoutMs 0 snapshots immediately. Wait on remaining running taskIds after consuming results. "
                + "timedOut means this bounded wait elapsed while work continues. Cancellation or a new user message ends only this wait. Results remain replayable until the parent run ends."
        });

        [McpServerTool(Name = "subtask_wait", ReadOnly = true, Destructive = false, Idempotent = true,
            OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(SubtaskWaitResult))]
        private async Task<CallToolResult> WaitAsync([MinLength(1)] string[] taskIds,
            IProgress<ProgressNotificationValue> progress,
            [RegularExpression("^(any|all)$")] string mode = "any",
            [Range(0, 60000)] int waitTimeoutMs = 60000, CancellationToken cancellationToken = default)
        {
            if (mode is not ("any" or "all")) return reply.Reply(new SubtaskWaitResult([], "Choose any or all."), true);
            try
            {
                var result = await tasks.WaitAsync(run, taskIds, mode == "all", waitTimeoutMs,
                    new ForwardProgress(progress), cancellationToken);
                return reply.Reply(result, new SubtaskTranscript(tasks.Transcript(run, taskIds)), false);
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            {
                return reply.Reply(new SubtaskWaitResult([], error.Message), true);
            }
        }
    }

    private sealed class ForwardProgress(IProgress<ProgressNotificationValue> sink) : IProgress<SubtaskProgress>
    {
        public void Report(SubtaskProgress value) => sink.Report(new ProgressNotificationValue
            { Progress = value.Done, Total = value.Total, Message = value.Message });
    }
}
