namespace AI.Mcp.BuiltIn.Triggers;

using System.Diagnostics;
using Grants;

public sealed class TriggerWaiter(IPathGuard guard) : ITriggerWaiter
{
    public const int MaxConditions = 8;
    public const int MaxTimeoutMs = 600000;
    private const int PollIntervalMs = 250;
    private const int MaxStableForMs = 60000;

    public async Task<TriggerWaitResult> WaitAsync(TriggerCondition[] conditions, int timeoutMs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        if (conditions.Length is < 1 or > MaxConditions)
            throw new ArgumentException($"Provide one to {MaxConditions} conditions.", nameof(conditions));
        if (timeoutMs is < 1 or > MaxTimeoutMs)
            throw new ArgumentOutOfRangeException(nameof(timeoutMs));

        using var signal = new SemaphoreSlim(0, 1);
        var states = new List<ConditionState>(conditions.Length);
        var watchOverflow = 0;
        var clock = Stopwatch.StartNew();
        try
        {
            for (var index = 0; index < conditions.Length; index++)
                states.Add(Prepare(conditions[index], index, signal, () => Interlocked.Exchange(ref watchOverflow, 1)));

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var state in states)
                    if (Evaluate(state, clock.ElapsedMilliseconds) is { } match)
                        return match with { ElapsedMs = clock.ElapsedMilliseconds, WatcherOverflow = Volatile.Read(ref watchOverflow) != 0 };

                var elapsed = clock.ElapsedMilliseconds;
                if (elapsed >= timeoutMs)
                    return new TriggerWaitResult("timeout", null, null, null, null, null, null, elapsed,
                        Volatile.Read(ref watchOverflow) != 0);

                var waitMs = (int)Math.Min(PollIntervalMs, timeoutMs - elapsed);
                foreach (var state in states)
                {
                    if (state.Kind == TriggerKind.Delay && state.Condition.AfterMs is { } afterMs && afterMs > elapsed)
                        waitMs = (int)Math.Min(waitMs, afterMs - elapsed);
                    if (state.EligibleSinceMs is { } eligible && state.Condition.StableForMs > 0)
                    {
                        var due = eligible + state.Condition.StableForMs - elapsed;
                        if (due > 0) waitMs = (int)Math.Min(waitMs, due);
                    }
                }

                await signal.WaitAsync(Math.Max(1, waitMs), cancellationToken);
            }
        }
        finally
        {
            foreach (var state in states) state.Dispose();
        }
    }

    private ConditionState Prepare(TriggerCondition condition, int index, SemaphoreSlim signal, Action overflow)
    {
        if (condition is null) throw new ArgumentException($"Condition {index} is null.");
        if (condition.StableForMs is < 0 or > MaxStableForMs)
            throw new ArgumentException($"Condition {index} has an invalid stableForMs.");

        var kind = condition.Type?.Trim().ToLowerInvariant() switch
        {
            "delay" => TriggerKind.Delay,
            "file_exists" => TriggerKind.FileExists,
            "file_missing" => TriggerKind.FileMissing,
            "file_changed" => TriggerKind.FileChanged,
            "process_exit" => TriggerKind.ProcessExit,
            "process_cpu_below" => TriggerKind.ProcessCpuBelow,
            "process_cpu_above" => TriggerKind.ProcessCpuAbove,
            "process_memory_below" => TriggerKind.ProcessMemoryBelow,
            "process_memory_above" => TriggerKind.ProcessMemoryAbove,
            _ => throw new ArgumentException($"Condition {index} has an unknown type.")
        };
        var state = new ConditionState(condition, kind, index);
        if (kind == TriggerKind.Delay)
        {
            if (condition.AfterMs is not > 0 or > MaxTimeoutMs || condition.StableForMs != 0)
                throw new ArgumentException($"Condition {index} requires afterMs in 1..{MaxTimeoutMs} and no stableForMs.");
            return state;
        }

        if (kind is TriggerKind.FileExists or TriggerKind.FileMissing or TriggerKind.FileChanged)
        {
            if (string.IsNullOrWhiteSpace(condition.Path))
                throw new ArgumentException($"Condition {index} requires an absolute path.");
            state.Path = guard.Resolve(condition.Path, GrantCapability.Read);
            if (Directory.Exists(state.Path))
                throw new ArgumentException($"Condition {index} must name a file, not a directory.");
            state.Baseline = Snapshot(state.Path);
            state.LastFile = state.Baseline;
            var directory = Path.GetDirectoryName(state.Path);
            while (directory is not null && !Directory.Exists(directory)) directory = Path.GetDirectoryName(directory);
            if (directory is null)
                throw new ArgumentException($"Condition {index} has no existing parent directory.");
            guard.Resolve(directory, GrantCapability.Read);
            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = !string.Equals(directory, Path.GetDirectoryName(state.Path), PathComparison),
                Filter = string.Equals(directory, Path.GetDirectoryName(state.Path), PathComparison)
                    ? Path.GetFileName(state.Path) : "*",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            state.Watcher = watcher;
            void FileEvent(string candidate)
            {
                if (string.Equals(candidate, state.Path, PathComparison))
                    Interlocked.Increment(ref state.FileEvents);
                else if (!state.Path.StartsWith(candidate.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                             PathComparison))
                    return;
                Wake(signal);
            }
            FileSystemEventHandler changed = (_, args) => FileEvent(args.FullPath);
            RenamedEventHandler renamed = (_, args) => { FileEvent(args.OldFullPath); FileEvent(args.FullPath); };
            ErrorEventHandler error = (_, _) => { overflow(); Wake(signal); };
            watcher.Changed += changed;
            watcher.Created += changed;
            watcher.Deleted += changed;
            watcher.Renamed += renamed;
            watcher.Error += error;
            try { watcher.EnableRaisingEvents = true; }
            catch { watcher.Dispose(); throw; }
            return state;
        }

        if (condition.ProcessId is not > 0)
            throw new ArgumentException($"Condition {index} requires a positive processId.");
        if (kind is TriggerKind.ProcessCpuBelow or TriggerKind.ProcessCpuAbove
            && condition.CpuPercent is (null or < 0 or > 100))
            throw new ArgumentException($"Condition {index} requires cpuPercent in 0..100.");
        if (kind is TriggerKind.ProcessMemoryBelow or TriggerKind.ProcessMemoryAbove
            && condition.MemoryBytes is (null or < 0))
            throw new ArgumentException($"Condition {index} requires nonnegative memoryBytes.");
        if (kind == TriggerKind.ProcessExit && condition.StableForMs != 0)
            throw new ArgumentException($"Condition {index} does not support stableForMs.");
        state.Process = System.Diagnostics.Process.GetProcessById(condition.ProcessId.Value);
        return state;
    }

    private TriggerWaitResult? Evaluate(ConditionState state, long elapsedMs)
    {
        var condition = state.Condition;
        if (state.Kind == TriggerKind.Delay)
            return elapsedMs >= condition.AfterMs
                ? Match(state, elapsedMs) : null;

        if (state.Path is { } path)
        {
            // Recheck the observed path in case a link appears after registration.
            guard.Resolve(path, GrantCapability.Read);
            var snapshot = Snapshot(path);
            var newEvent = Interlocked.Exchange(ref state.FileEvents, 0) != 0;
            var changed = newEvent || snapshot != state.LastFile;
            if (newEvent) state.SawFileChange = true;
            state.SawFileChange |= snapshot != state.Baseline;
            if (changed) state.EligibleSinceMs = null;
            state.LastFile = snapshot;
            var eligible = state.Kind switch
            {
                TriggerKind.FileExists => snapshot.Exists,
                TriggerKind.FileMissing => !snapshot.Exists,
                TriggerKind.FileChanged => state.SawFileChange,
                _ => false
            };
            return Eligible(state, eligible, elapsedMs);
        }

        var process = state.Process!;
        process.Refresh();
        if (process.HasExited)
            return state.Kind == TriggerKind.ProcessExit ? Match(state, elapsedMs) : null;
        if (state.Kind == TriggerKind.ProcessExit) return null;

        state.MemoryBytes = process.WorkingSet64;
        if (state.Kind is TriggerKind.ProcessMemoryBelow or TriggerKind.ProcessMemoryAbove)
            return Eligible(state, state.Kind == TriggerKind.ProcessMemoryBelow
                ? state.MemoryBytes < condition.MemoryBytes : state.MemoryBytes > condition.MemoryBytes, elapsedMs);

        var cpuTime = process.TotalProcessorTime;
        if (state.PreviousCpuTime is null)
        {
            state.PreviousCpuTime = cpuTime;
            state.PreviousSampleMs = elapsedMs;
            return null;
        }
        var intervalMs = elapsedMs - state.PreviousSampleMs;
        if (intervalMs <= 0) return null;
        state.CpuPercent = Math.Clamp((cpuTime - state.PreviousCpuTime.Value).TotalMilliseconds
                                      / (intervalMs * Environment.ProcessorCount) * 100, 0, 100);
        state.PreviousCpuTime = cpuTime;
        state.PreviousSampleMs = elapsedMs;
        return Eligible(state, state.Kind == TriggerKind.ProcessCpuBelow
            ? state.CpuPercent < condition.CpuPercent : state.CpuPercent > condition.CpuPercent, elapsedMs);
    }

    private static TriggerWaitResult? Eligible(ConditionState state, bool eligible, long elapsedMs)
    {
        if (!eligible)
        {
            state.EligibleSinceMs = null;
            return null;
        }
        state.EligibleSinceMs ??= elapsedMs;
        return elapsedMs - state.EligibleSinceMs >= state.Condition.StableForMs ? Match(state, elapsedMs) : null;
    }

    private static TriggerWaitResult Match(ConditionState state, long elapsedMs) =>
        new("triggered", state.Index, state.Condition.Type, state.Path, state.Condition.ProcessId,
            state.CpuPercent, state.MemoryBytes, elapsedMs, false);

    private static FileStamp Snapshot(string path)
    {
        var info = new FileInfo(path);
        info.Refresh();
        return info.Exists ? new FileStamp(true, info.Length, info.LastWriteTimeUtc.Ticks) : default;
    }

    private static void Wake(SemaphoreSlim signal)
    {
        try { signal.Release(); }
        catch (SemaphoreFullException) { }
        catch (ObjectDisposedException) { }
    }

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly record struct FileStamp(bool Exists, long Length, long LastWriteTicks);

    private enum TriggerKind
    {
        Delay, FileExists, FileMissing, FileChanged, ProcessExit,
        ProcessCpuBelow, ProcessCpuAbove, ProcessMemoryBelow, ProcessMemoryAbove
    }

    private sealed class ConditionState(TriggerCondition condition, TriggerKind kind, int index) : IDisposable
    {
        public TriggerCondition Condition { get; } = condition;
        public TriggerKind Kind { get; } = kind;
        public int Index { get; } = index;
        public string? Path { get; set; }
        public FileSystemWatcher? Watcher { get; set; }
        public System.Diagnostics.Process? Process { get; set; }
        public FileStamp Baseline { get; set; }
        public FileStamp LastFile { get; set; }
        public int FileEvents;
        public bool SawFileChange { get; set; }
        public long? EligibleSinceMs { get; set; }
        public TimeSpan? PreviousCpuTime { get; set; }
        public long PreviousSampleMs { get; set; }
        public double? CpuPercent { get; set; }
        public long? MemoryBytes { get; set; }

        public void Dispose()
        {
            Watcher?.Dispose();
            Process?.Dispose();
        }
    }
}
