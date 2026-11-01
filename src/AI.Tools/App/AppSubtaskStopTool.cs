namespace AI.Mcp.App;

using System.ComponentModel.DataAnnotations;
using AI.Application.Tools;
using AI.Contracts.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class AppSubtaskStopTool(ISubtaskRuns tasks) : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => new Session(tasks, run, reply).Create();

    private sealed class Session(ISubtaskRuns tasks, ToolRunContext run, IAppToolReply reply)
    {
        public McpServerTool Create() => McpServerTool.Create(StopAsync, new McpServerToolCreateOptions
        {
            SerializerOptions = reply.Json,
            Description = "Cancel selected subtask handles owned by this parent run, wait for cancellation to settle, "
                + "and return their terminal results. Finished tasks retain their result. Other tasks are unaffected."
        });

        [McpServerTool(Name = "subtask_stop", ReadOnly = false, Destructive = false, Idempotent = true,
            OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(SubtaskWaitResult))]
        private async Task<CallToolResult> StopAsync([MinLength(1)] string[] taskIds, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await tasks.StopAsync(run, taskIds, cancellationToken);
                return reply.Reply(result, new SubtaskTranscript(tasks.Transcript(run, taskIds)), false);
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            {
                return reply.Reply(new SubtaskWaitResult([], error.Message), true);
            }
        }
    }
}
