namespace AI.Application.Tools;

using AI.Contracts.Tools;
using System.Text.Json;

/// <summary>Delegated work belongs to one parent run, independently of individual MCP calls.</summary>
public interface ISubtaskRuns
{
    IAsyncDisposable Begin(ToolRunContext owner, CancellationToken cancellationToken);
    int NextConnection(ToolRunContext owner);
    SubtaskSnapshot Start(ToolRunContext owner, string task,
        Func<Action<string>, CancellationToken, Task<SubtaskCompletion>> execute);
    Task<SubtaskWaitResult> WaitAsync(ToolRunContext owner, IReadOnlyList<string> taskIds, bool all,
        int waitTimeoutMs, IProgress<SubtaskProgress>? progress, CancellationToken cancellationToken);
    Task<SubtaskWaitResult> StopAsync(ToolRunContext owner, IReadOnlyList<string> taskIds, CancellationToken cancellationToken);
    IReadOnlyList<SubtaskSnapshot> Outstanding(ToolRunContext owner);
    IReadOnlyList<SubtaskTranscriptEntry> Transcript(ToolRunContext owner, IReadOnlyList<string> taskIds);
    void Acknowledge(ToolRunContext owner, JsonElement? result);
    Task StopAllAsync(ToolRunContext owner);
}

public sealed record SubtaskProgress(int Done, int Total, string Message);
