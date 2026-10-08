namespace AI.Mcp.BuiltIn.Triggers;

public sealed record TriggerCondition(
    string Type,
    string? Path = null,
    int? ProcessId = null,
    int? AfterMs = null,
    double? CpuPercent = null,
    long? MemoryBytes = null,
    int StableForMs = 0);

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
    string? Error = null);

public interface ITriggerWaiter
{
    Task<TriggerWaitResult> WaitAsync(TriggerCondition[] conditions, int timeoutMs, CancellationToken cancellationToken);
}
