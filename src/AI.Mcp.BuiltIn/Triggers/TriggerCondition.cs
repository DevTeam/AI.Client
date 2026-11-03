namespace AI.Mcp.BuiltIn.Triggers;

using Process;

public sealed record TriggerCondition(
    string Type,
    string? Path = null,
    int? ProcessId = null,
    int? AfterMs = null,
    double? CpuPercent = null,
    long? MemoryBytes = null,
    int StableForMs = 0,
    string? ProcessSessionId = null,
    long StdoutOffset = 0,
    long StderrOffset = 0,
    string? Pattern = null,
    bool Regex = false,
    bool IgnoreCase = false,
    int? QuietMs = null,
    int? Depth = null);

/// <summary>
/// What one condition looked like when the wait ended. A timeout that says nothing leaves the model
/// guessing whether the file was missing or the process already gone, so every result carries the
/// last state of every condition, including the one that matched.
/// </summary>
/// <param name="ConditionIndex">Zero-based position in the requested conditions.</param>
/// <param name="Type">The condition type as it was asked for.</param>
/// <param name="Path">The canonical path a file condition watched, otherwise <c>null</c>.</param>
/// <param name="Exists">Whether the file existed when the state was read.</param>
/// <param name="LengthBytes">File length in bytes when it existed.</param>
/// <param name="LastWriteTime">Last write time of the file when it existed.</param>
/// <param name="ProcessId">The OS process a process condition watched.</param>
/// <param name="ProcessRunning">Whether that process was still alive.</param>
/// <param name="CpuPercent">The last sampled CPU percentage, normalized across logical processors.</param>
/// <param name="MemoryBytes">The last sampled working set of the process.</param>
/// <param name="ProcessStatus">Status of a managed execution: <c>running</c> until it finishes.</param>
/// <param name="Matched">Whether a text condition found its pattern in the text it read.</param>
/// <param name="Error">Why the state could not be read, if it could not.</param>
/// <param name="SearchedBytes">
/// How much text a text condition actually examined. A file is read from its end, so this is the
/// window that was searched, not the file's length.
/// </param>
/// <param name="OlderTextUnsearched">
/// Whether text older than the searched window was never examined, either because the file is
/// longer than that window or because the condition started from a nonzero output offset. A miss
/// with this set means the pattern may sit outside what was read, which is not the same as the
/// pattern never having been written.
/// </param>
public sealed record TriggerObservation(
    int ConditionIndex,
    string Type,
    string? Path = null,
    bool? Exists = null,
    long? LengthBytes = null,
    DateTimeOffset? LastWriteTime = null,
    int? ProcessId = null,
    bool? ProcessRunning = null,
    double? CpuPercent = null,
    long? MemoryBytes = null,
    string? ProcessStatus = null,
    string? Error = null,
    bool? Matched = null,
    long? SearchedBytes = null,
    bool? OlderTextUnsearched = null);

/// <summary>How far the wait has come, reported as <c>notifications/progress</c>.</summary>
/// <param name="ElapsedMs">Time since the wait started.</param>
/// <param name="TimeoutMs">The wait's own limit, which also bounds the progress total.</param>
/// <param name="Conditions">How many conditions are being watched together.</param>
/// <param name="Message">One short line naming what is being waited for.</param>
public sealed record TriggerProgress(int ElapsedMs, int TimeoutMs, int Conditions, string Message);

public sealed record TriggerWaitResult(
    string Outcome,
    int? ConditionIndex,
    string? Type,
    string? Path,
    int? ProcessId,
    double? CpuPercent,
    long? MemoryBytes,
    long ElapsedMs,
    bool WatcherOverflow,
    string? Error = null,
    ProcessResult? ProcessResult = null,
    int? TimeoutMs = null,
    TriggerObservation[]? Observed = null);

public interface ITriggerWaiter
{
    Task<TriggerWaitResult> WaitAsync(TriggerCondition[] conditions, int timeoutMs,
        IProgress<TriggerProgress>? progress, CancellationToken cancellationToken);
}
