namespace AI.Mcp.BuiltIn.Triggers;

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AI.Contracts.FileSystem;
using Grants;
using Process;

public sealed class TriggerWaiter : ITriggerWaiter
{
    public const int MaxConditions = 8;
    public const int MaxTimeoutMs = 86400000;

    /// <summary>
    /// How much of a file a text condition reads. A log is read from its end, so a build log that has
    /// grown to a gigabyte does not cost a gigabyte on every poll; the tail is where the newest
    /// lines are anyway.
    /// </summary>
    public const int TailBytes = 256 * 1024;

    /// <summary>
    /// Characters kept from the previous page of managed output, enough for a pattern written across
    /// two pages to still read as one match.
    /// </summary>
    private const int OutputCarryChars = 1024;

    private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(1);

    private const int PollIntervalMs = 250;
    private const int ProgressIntervalMs = 1000;

    private readonly IPathGuard _guard;
    private readonly IFileSystem _files;
    private readonly IPath _paths;
    private readonly IProcessSessions? _processes;
    private readonly IFileWatcherFactory _watchers;

    /// <param name="guard">The grants a condition path has to pass before it is watched.</param>
    /// <param name="files">
    /// The file system the snapshots are taken through. Optional so that a caller which already knows
    /// it wants the platform does not have to say so; the composition injects the bound implementations.
    /// </param>
    /// <param name="paths">The path semantics this waiter judges with.</param>
    /// <param name="processes">Managed executions owned by this MCP session.</param>
    /// <param name="watchers">
    /// The platform watchers. Optional for the same reason as the file system: the composition binds
    /// the platform implementation, a test binds one it can count.
    /// </param>
    public TriggerWaiter(IPathGuard guard, IFileSystem? files = null, IPath? paths = null,
        IProcessSessions? processes = null, IFileWatcherFactory? watchers = null)
    {
        ArgumentNullException.ThrowIfNull(guard);
        _guard = guard;
        _files = files ?? new SystemFileSystem();
        _paths = paths ?? new SystemPath();
        _processes = processes;
        _watchers = watchers ?? new SystemFileWatcherFactory();
    }

    public async Task<TriggerWaitResult> WaitAsync(TriggerCondition[] conditions, int timeoutMs,
        IProgress<TriggerProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        if (conditions.Length is < 1 or > MaxConditions)
            throw new ArgumentException($"Provide one to {MaxConditions} conditions.", nameof(conditions));
        if (timeoutMs is < 1 or > MaxTimeoutMs)
            throw new ArgumentOutOfRangeException(nameof(timeoutMs));

        using var signal = new SemaphoreSlim(0, 1);
        using var observers = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var watches = new FileWatchRegistry(_watchers);
        var states = new List<ConditionState>(conditions.Length);
        var watchOverflow = 0;
        var clock = Stopwatch.StartNew();
        try
        {
            for (var index = 0; index < conditions.Length; index++)
                states.Add(await PrepareAsync(conditions[index], index, signal, watches,
                    () => Interlocked.Exchange(ref watchOverflow, 1), observers.Token));

            var needsPolling = states.Any(state =>
                state.Kind is not (TriggerKind.Delay or TriggerKind.ProcessCompleted or TriggerKind.ProcessExit));
            var lastReportMs = 0L;
            Report(progress, 0, timeoutMs, states);
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException("The wait was cancelled.", cancellationToken);
                foreach (var state in states)
                    if (await EvaluateAsync(state, clock.ElapsedMilliseconds, cancellationToken) is { } match)
                        return match with
                        {
                            ElapsedMs = clock.ElapsedMilliseconds,
                            WatcherOverflow = Volatile.Read(ref watchOverflow) != 0,
                            TimeoutMs = timeoutMs,
                            Observed = Observations(states)
                        };

                var elapsed = clock.ElapsedMilliseconds;
                if (elapsed >= timeoutMs)
                    return new TriggerWaitResult("timeout", null, null, null, null, null, null, elapsed,
                        Volatile.Read(ref watchOverflow) != 0, TimeoutMs: timeoutMs, Observed: Observations(states));

                if (elapsed - lastReportMs >= ProgressIntervalMs)
                {
                    Report(progress, elapsed, timeoutMs, states);
                    lastReportMs = elapsed;
                }

                var waitMs = (int)Math.Min(needsPolling ? PollIntervalMs : timeoutMs, timeoutMs - elapsed);
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

                // A reported wait wakes for its next report even when nothing can change meanwhile:
                // a delay of a minute used to be one long silence with no sign of life.
                if (progress is not null)
                    waitMs = (int)Math.Min(waitMs, Math.Max(1, ProgressIntervalMs - (elapsed - lastReportMs)));

                await signal.WaitAsync(Math.Max(1, waitMs), cancellationToken);
            }
        }
        finally
        {
            await observers.CancelAsync();
            await Task.WhenAll(states.SelectMany(state => new[] { state.CompletionObserver, state.ExitObserver })
                .OfType<Task>());
            foreach (var state in states) state.Dispose();
            watches.Dispose();
        }
    }

    /// <summary>
    /// What one condition is waiting for, in a line the person reading the tool card understands.
    /// The message is the only part of a progress report a human sees.
    /// </summary>
    private static void Report(IProgress<TriggerProgress>? progress, long elapsedMs, int timeoutMs,
        List<ConditionState> states)
    {
        if (progress is null) return;
        var waiting = string.Join(" or ", states.Select(state => state.Kind switch
        {
            TriggerKind.Delay => $"a delay of {state.Condition.AfterMs} ms",
            TriggerKind.FileExists => $"the file {state.Path} to exist",
            TriggerKind.FileMissing => $"the file {state.Path} to disappear",
            TriggerKind.FileChanged => $"the file {state.Path} to change",
            TriggerKind.FileContains => $"the file {state.Path} to contain "
                + (state.Condition.Regex ? $"a match for '{state.Condition.Pattern}'" : $"'{state.Condition.Pattern}'"),
            TriggerKind.FileIdle => $"the file {state.Path} to stay unchanged for {state.Condition.QuietMs} ms",
            TriggerKind.ProcessExit => $"process {state.Condition.ProcessId} to exit",
            TriggerKind.ProcessCompleted => "the managed process to finish",
            TriggerKind.ProcessOutputContains => "the managed process output to contain "
                + (state.Condition.Regex ? $"a match for '{state.Condition.Pattern}'" : $"'{state.Condition.Pattern}'"),
            TriggerKind.ProcessCpuBelow => $"process {state.Condition.ProcessId} to fall below {state.Condition.CpuPercent}% CPU",
            TriggerKind.ProcessCpuAbove => $"process {state.Condition.ProcessId} to rise above {state.Condition.CpuPercent}% CPU",
            TriggerKind.ProcessMemoryBelow => $"process {state.Condition.ProcessId} to fall below {state.Condition.MemoryBytes} bytes",
            _ => $"process {state.Condition.ProcessId} to rise above {state.Condition.MemoryBytes} bytes"
        }));
        progress.Report(new TriggerProgress((int)Math.Min(elapsedMs, int.MaxValue), timeoutMs, states.Count,
            $"Waiting for {waiting}."));
    }

    private static TriggerObservation[] Observations(List<ConditionState> states) =>
        states.Select(state => new TriggerObservation(state.Index, state.Condition.Type, state.Path, state.Exists,
            state.LengthBytes, state.LastWriteTime, state.Condition.ProcessId, state.ProcessRunning, state.CpuPercent,
            state.MemoryBytes, state.ProcessStatus, state.Error, state.Matched, state.SearchedBytes,
            state.OlderTextUnsearched)).ToArray();

    /// <summary>Names the condition in an error, so a model fixes the right one of up to eight.</summary>
    private static string Label(int index, string? type) => $"Condition {index} ('{type}')";

    private async Task<ConditionState> PrepareAsync(
        TriggerCondition condition, int index, SemaphoreSlim signal, FileWatchRegistry watches, Action overflow,
        CancellationToken cancellationToken)
    {
        if (condition is null) throw new ArgumentException($"Condition {index} is null.");
        var kind = condition.Type?.Trim().ToLowerInvariant() switch
        {
            "delay" => TriggerKind.Delay,
            "file_exists" => TriggerKind.FileExists,
            "file_missing" => TriggerKind.FileMissing,
            "file_changed" => TriggerKind.FileChanged,
            "file_contains" => TriggerKind.FileContains,
            "file_idle" => TriggerKind.FileIdle,
            "process_exit" => TriggerKind.ProcessExit,
            "process_completed" => TriggerKind.ProcessCompleted,
            "process_output_contains" => TriggerKind.ProcessOutputContains,
            "process_cpu_below" => TriggerKind.ProcessCpuBelow,
            "process_cpu_above" => TriggerKind.ProcessCpuAbove,
            "process_memory_below" => TriggerKind.ProcessMemoryBelow,
            "process_memory_above" => TriggerKind.ProcessMemoryAbove,
            _ => throw new ArgumentException(Label(index, condition.Type)
                + " has an unknown type. Use delay, file_exists, file_missing, file_changed, file_contains, file_idle, "
                + "process_exit, process_completed, process_output_contains, process_cpu_below, process_cpu_above, "
                + "process_memory_below or process_memory_above.")
        };
        var label = Label(index, condition.Type);
        RejectUnrelatedFields(condition, kind, label, index);

        var state = new ConditionState(condition, kind, index);
        if (condition.StableForMs is < 0 or > MaxTimeoutMs)
            throw new ArgumentException($"{label} has stableForMs outside 0..{MaxTimeoutMs}.");
        RequireFields(condition, kind, label);

        if (kind == TriggerKind.ProcessCompleted)
        {
            if (_processes is null)
                throw new InvalidOperationException("Managed process observation is unavailable.");
            state.Completion = _processes.WaitForCompletionAsync(condition.ProcessSessionId!,
                condition.StdoutOffset, condition.StderrOffset, cancellationToken);
            state.CompletionObserver = WakeOnCompletionAsync(state.Completion, signal);
            return state;
        }

        if (kind == TriggerKind.Delay)
            return state;

        if (kind == TriggerKind.ProcessOutputContains)
        {
            if (_processes is null)
                throw new InvalidOperationException("Managed process observation is unavailable.");
            // One snapshot now: it proves the handle belongs to this session, and a process that has
            // already finished is read from the same page the first evaluation will search, so its
            // result carries the text that matched. The offsets therefore stay as they were asked for.
            var page = await _processes.WaitAsync(condition.ProcessSessionId!, 0, condition.StdoutOffset,
                condition.StderrOffset, cancellationToken);
            if (page.Status == "failed")
                throw new ArgumentException($"{label}: {page.Error}");
            state.ProcessStatus = page.Status;
            return state;
        }

        if (kind is TriggerKind.FileExists or TriggerKind.FileMissing or TriggerKind.FileChanged
            or TriggerKind.FileContains or TriggerKind.FileIdle)
        {
            state.Path = _guard.Resolve(condition.Path!, GrantCapability.Read);
            if (await _files.DirectoryExistsAsync(state.Path, cancellationToken))
                throw new ArgumentException($"{label} must name a file, not a directory.");
            state.Baseline = await SnapshotAsync(state.Path, cancellationToken);
            state.LastFile = state.Baseline;
            Observe(state, state.Baseline);
            // A text condition asks for the text once before it starts watching: a file that already
            // carries the pattern is a match now, not something to wait for.
            if (kind == TriggerKind.FileContains)
                state.Matched = await ContainsAsync(state.Path, condition, state, cancellationToken);
            var directory = _paths.GetDirectoryName(state.Path);
            while (directory is not null && !await _files.DirectoryExistsAsync(directory, cancellationToken))
                directory = _paths.GetDirectoryName(directory);
            if (directory is null)
                throw new ArgumentException($"{label} has no existing parent directory.");
            _guard.Resolve(directory, GrantCapability.Read);
            var watched = state.Path;
            var ownDirectory = _paths.GetDirectoryName(watched);
            // The watcher itself stays on the platform: it is a kernel notification, not a file
            // operation, and one of the ADR's documented exceptions says so. Everything it is told
            // is judged through the contract. Conditions watching the same directory share it.
            state.Watch = watches.Add(directory, !string.Equals(directory, ownDirectory, _paths.Comparison), watched,
                candidate =>
                {
                    if (string.Equals(candidate, watched, _paths.Comparison)) Interlocked.Increment(ref state.FileEvents);
                    else if (!_paths.IsInside(watched, candidate, recursive: true)) return;
                    Wake(signal);
                },
                () =>
                {
                    overflow();
                    Wake(signal);
                });
            return state;
        }

        state.Process = System.Diagnostics.Process.GetProcessById(condition.ProcessId!.Value);
        state.BoundAt = StartTime(state.Process);
        if (kind == TriggerKind.ProcessExit)
        {
            // Waiting on the kernel's own exit notification instead of polling every quarter second:
            // a process that ends just after the wait starts used to cost a full poll interval.
            state.Exit = state.Process.WaitForExitAsync(cancellationToken);
            state.ExitObserver = WakeOnExitAsync(state.Exit, signal);
        }
        return state;
    }

    /// <summary>
    /// A condition that carries a field its type does not read is a mistake, not a detail to ignore:
    /// <c>stableMs</c> for <c>stableForMs</c> would silently watch for an immediate change, and
    /// <c>cpuPercent</c> next to <c>file_exists</c> would promise a threshold nothing compares.
    /// </summary>
    private static void RejectUnrelatedFields(TriggerCondition condition, TriggerKind kind, string label, int index)
    {
        var allowed = kind switch
        {
            TriggerKind.Delay => new[] { "afterMs" },
            TriggerKind.FileExists or TriggerKind.FileMissing or TriggerKind.FileChanged => ["path", "stableForMs"],
            TriggerKind.FileContains => ["path", "pattern", "regex", "ignoreCase", "stableForMs"],
            TriggerKind.FileIdle => ["path", "quietMs", "stableForMs"],
            TriggerKind.ProcessExit => ["processId"],
            TriggerKind.ProcessCompleted => ["processSessionId", "stdoutOffset", "stderrOffset"],
            TriggerKind.ProcessOutputContains => ["processSessionId", "pattern", "regex", "ignoreCase",
                "stdoutOffset", "stderrOffset"],
            TriggerKind.ProcessCpuBelow or TriggerKind.ProcessCpuAbove => ["processId", "cpuPercent", "stableForMs"],
            _ => ["processId", "memoryBytes", "stableForMs"]
        };
        var unexpected = new List<string>();
        void Check(string name, bool set)
        {
            if (set && !allowed.Contains(name)) unexpected.Add(name);
        }

        Check("path", !string.IsNullOrWhiteSpace(condition.Path));
        Check("processId", condition.ProcessId is not null);
        Check("afterMs", condition.AfterMs is not null);
        Check("cpuPercent", condition.CpuPercent is not null);
        Check("memoryBytes", condition.MemoryBytes is not null);
        Check("stableForMs", condition.StableForMs != 0);
        Check("processSessionId", !string.IsNullOrWhiteSpace(condition.ProcessSessionId));
        Check("stdoutOffset", condition.StdoutOffset != 0);
        Check("stderrOffset", condition.StderrOffset != 0);
        Check("pattern", !string.IsNullOrEmpty(condition.Pattern));
        Check("regex", condition.Regex);
        Check("ignoreCase", condition.IgnoreCase);
        Check("quietMs", condition.QuietMs is not null);
        if (unexpected.Count == 0) return;
        throw new ArgumentException($"{label} does not read {string.Join(", ", unexpected)}; condition {index} takes "
            + string.Join(", ", allowed) + ".");
    }

    /// <summary>What a type cannot work without, checked before anything is watched.</summary>
    private static void RequireFields(TriggerCondition condition, TriggerKind kind, string label)
    {
        switch (kind)
        {
            case TriggerKind.Delay when condition.AfterMs is not > 0 or > MaxTimeoutMs:
                throw new ArgumentException($"{label} requires afterMs in 1..{MaxTimeoutMs}.");
            case TriggerKind.Delay:
                return;
            case TriggerKind.ProcessCompleted when string.IsNullOrWhiteSpace(condition.ProcessSessionId):
                throw new ArgumentException($"{label} requires processSessionId from a process_run result.");
            case TriggerKind.ProcessCompleted:
                return;
            case TriggerKind.ProcessOutputContains when string.IsNullOrWhiteSpace(condition.ProcessSessionId):
                throw new ArgumentException($"{label} requires processSessionId from a process_run result.");
        }

        if (kind is TriggerKind.FileExists or TriggerKind.FileMissing or TriggerKind.FileChanged
            or TriggerKind.FileContains or TriggerKind.FileIdle)
        {
            if (string.IsNullOrWhiteSpace(condition.Path))
                throw new ArgumentException($"{label} requires an absolute path.");
            if (kind == TriggerKind.FileIdle && condition.QuietMs is not > 0 or > MaxTimeoutMs)
                throw new ArgumentException($"{label} requires quietMs in 1..{MaxTimeoutMs}.");
        }
        else if (kind != TriggerKind.ProcessOutputContains)
        {
            if (condition.ProcessId is not > 0)
                throw new ArgumentException($"{label} requires a positive processId.");
            if (kind is TriggerKind.ProcessCpuBelow or TriggerKind.ProcessCpuAbove
                && condition.CpuPercent is null or < 0 or > 100)
                throw new ArgumentException($"{label} requires cpuPercent in 0..100.");
            if (kind is TriggerKind.ProcessMemoryBelow or TriggerKind.ProcessMemoryAbove
                && condition.MemoryBytes is null or < 0)
                throw new ArgumentException($"{label} requires nonnegative memoryBytes.");
        }

        if (kind is not (TriggerKind.FileContains or TriggerKind.ProcessOutputContains)) return;
        if (string.IsNullOrEmpty(condition.Pattern))
            throw new ArgumentException($"{label} requires pattern.");
        if (!condition.Regex) return;
        try
        {
            _ = new Regex(condition.Pattern, PatternOptions(condition), PatternTimeout);
        }
        catch (ArgumentException error)
        {
            throw new ArgumentException($"{label} has an invalid regular expression: {error.Message}");
        }
    }

    private async Task<TriggerWaitResult?> EvaluateAsync(ConditionState state, long elapsedMs, CancellationToken cancellationToken)
    {
        var condition = state.Condition;
        if (state.Completion is { } completion)
        {
            if (!completion.IsCompleted)
            {
                // Still running is the observation a timeout needs here: the managed handle was found
                // and watched, the command simply had not finished.
                state.ProcessStatus = "running";
                return null;
            }
            var finished = await completion;
            state.ProcessStatus = finished.Status;
            return Match(state, elapsedMs) with { ProcessResult = finished };
        }

        if (state.Kind == TriggerKind.ProcessOutputContains)
        {
            if (_processes is null) return null;
            var page = await _processes.WaitAsync(condition.ProcessSessionId!, 0, state.StdoutOffset,
                state.StderrOffset, cancellationToken);
            if (page.Status == "failed" && state.Matched != true)
            {
                state.Error = page.Error;
                return null;
            }
            state.ProcessStatus = page.Status;
            state.StdoutOffset = page.StdoutOffset;
            state.StderrOffset = page.StderrOffset;
            if (state.Matched != true) state.Matched = SearchOutput(state, page.Stdout, page.Stderr);
            if (state.Matched == true) return Match(state, elapsedMs) with { ProcessResult = page };
            // A finished command can print no more: its last page has already been searched. Reporting
            // that in the observation is what tells a timeout from a command that never said the line.
            if (page.Status is not "running") state.Error = "The process finished without printing the pattern.";
            return null;
        }

        if (state.Kind == TriggerKind.Delay)
            return elapsedMs >= condition.AfterMs
                ? Match(state, elapsedMs) : null;

        if (state.Path is { } path)
        {
            // Recheck the observed path in case a link appears after registration.
            _guard.Resolve(path, GrantCapability.Read);
            var snapshot = await SnapshotAsync(path, cancellationToken);
            Observe(state, snapshot);
            var newEvent = Interlocked.Exchange(ref state.FileEvents, 0) != 0;
            var changed = newEvent || snapshot != state.LastFile;
            if (newEvent) state.SawFileChange = true;
            state.SawFileChange |= snapshot != state.Baseline;
            if (changed)
            {
                // A snapshot that moved restarts the quiet period: file_idle asks for silence since
                // the last observed change, not since the wait started.
                state.EligibleSinceMs = null;
                state.LastChangeMs = elapsedMs;
            }
            state.LastFile = snapshot;
            var eligible = state.Kind switch
            {
                TriggerKind.FileExists => snapshot.Exists,
                TriggerKind.FileMissing => !snapshot.Exists,
                TriggerKind.FileChanged => state.SawFileChange,
                TriggerKind.FileIdle => !changed && elapsedMs - state.LastChangeMs >= condition.QuietMs,
                _ => false
            };
            // Reading the text is the expensive half of a text condition, so it happens only once the
            // file is known to be there, and only when the snapshot says something moved.
            if (state.Kind == TriggerKind.FileContains)
            {
                if (snapshot.Exists && (changed || state.Matched is null))
                    state.Matched = await ContainsAsync(path, condition, state, cancellationToken);
                eligible = state.Matched == true;
            }
            return Eligible(state, eligible, elapsedMs);
        }

        var process = state.Process!;
        if (!Running(state))
        {
            state.MemoryBytes = null;
            // A process that ended cannot reach a threshold, but it did do what process_exit waits for.
            if (state.Kind == TriggerKind.ProcessExit) return Match(state, elapsedMs);
            state.Error ??= "The process exited before the condition was satisfied.";
            return null;
        }

        long memory;
        try { memory = process.WorkingSet64; }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            state.Error = error.Message;
            return null;
        }

        state.MemoryBytes = memory;
        if (state.Kind is TriggerKind.ProcessMemoryBelow or TriggerKind.ProcessMemoryAbove)
            return Eligible(state, state.Kind == TriggerKind.ProcessMemoryBelow
                ? memory < condition.MemoryBytes : memory > condition.MemoryBytes, elapsedMs);

        TimeSpan cpuTime;
        try { cpuTime = process.TotalProcessorTime; }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            state.Error = error.Message;
            return null;
        }

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

    /// <summary>
    /// Searches one page of managed output, with the tail of the previous pages in front of it, so a
    /// pattern written across a page boundary still reads as one match. The tail kept is short and
    /// bounded; without it a model waiting for "BUILD SUCCEEDED" could miss it because the page ended
    /// in the middle of the phrase.
    /// </summary>
    private static bool SearchOutput(ConditionState state, string stdout, string stderr)
    {
        var page = (stdout.Length == 0 ? "" : stdout) + (stderr.Length == 0 ? "" : "\n" + stderr);
        // What this condition has looked at, across pages: a miss at the end then says whether the
        // whole output was read or only the part after the offsets it was given.
        state.SearchedBytes = (state.SearchedBytes ?? 0) + Encoding.UTF8.GetByteCount(page);
        state.OlderTextUnsearched = state.Condition.StdoutOffset > 0 || state.Condition.StderrOffset > 0;
        if (page.Length == 0) return false;
        var text = state.OutputTail is { Length: > 0 } tail ? tail + page : page;
        if (state.OutputTail is { Length: > 0 }) state.OutputTail = null;
        if (Matches(state.Condition, text)) return true;
        state.OutputTail = text.Length <= OutputCarryChars ? text : text[^OutputCarryChars..];
        return false;
    }

    /// <summary>Whether a file's own text carries the pattern, read from the end of the file.</summary>
    private async Task<bool> ContainsAsync(string path, TriggerCondition condition, ConditionState state,
        CancellationToken cancellationToken)
    {
        var (text, searched, unsearched) = await ReadTailAsync(path, cancellationToken);
        state.SearchedBytes = searched;
        state.OlderTextUnsearched = unsearched;
        return text is not null && Matches(condition, text);
    }

    /// <summary>
    /// The last <see cref="TailBytes"/> of a file as text, with the size of the window that was read
    /// and whether anything older was left unread. A writer holding the file against readers answers
    /// with no text at all: the next poll tries again and the observation keeps saying the file is
    /// there, which is different from a file that was read and did not match. A log is read from its
    /// end because that is both where the news is and the only part worth paying for: a build log
    /// that grew to a gigabyte is not re-read whole on every poll.
    /// </summary>
    private async Task<(string? Text, long SearchedBytes, bool OlderTextUnsearched)> ReadTailAsync(
        string path, CancellationToken cancellationToken)
    {
        Stream? stream;
        try { stream = await _files.OpenReadAsync(path, cancellationToken); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return (null, 0, false);
        }
        if (stream is null) return (null, 0, false);
        await using (stream)
        {
            var length = stream.CanSeek ? stream.Length : 0;
            if (stream.CanSeek && length > TailBytes) stream.Position = length - TailBytes;
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = await reader.ReadToEndAsync(cancellationToken);
            // Bytes, not characters: what a person compares with the size the file tool reports.
            var searched = Encoding.UTF8.GetByteCount(text);
            return (text, searched, stream.CanSeek && length > searched);
        }
    }

    private static bool Matches(TriggerCondition condition, string text)
    {
        if (text.Length == 0 || string.IsNullOrEmpty(condition.Pattern)) return false;
        if (!condition.Regex)
            return text.Contains(condition.Pattern,
                condition.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        try
        {
            return Regex.IsMatch(text, condition.Pattern, PatternOptions(condition), PatternTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            // A pattern that backtracks past the limit is a pattern problem, not a match; the next
            // poll tries again instead of ending a long wait.
            return false;
        }
    }

    private static RegexOptions PatternOptions(TriggerCondition condition) =>
        RegexOptions.CultureInvariant | (condition.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);

    /// <summary>
    /// Whether the process a condition was bound to is still that process. A PID is reused, so a
    /// process that ended can hand its number to a stranger whose CPU and memory would otherwise be
    /// reported as the finished one's. When this account may not read the start time, the answer
    /// falls back to liveness alone: the stronger check is unavailable, not negative.
    /// </summary>
    private static bool Running(ConditionState state)
    {
        var process = state.Process!;
        try
        {
            process.Refresh();
            if (process.HasExited)
            {
                state.ProcessRunning = false;
                return false;
            }
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            state.Error = error.Message;
            state.ProcessRunning = false;
            return false;
        }

        if (state.BoundAt is { } bound && StartTime(process) is { } started && started != bound)
        {
            state.ProcessRunning = false;
            state.Error = "The process identifier now belongs to another process.";
            return false;
        }

        state.ProcessRunning = true;
        return true;
    }

    private static DateTimeOffset? StartTime(System.Diagnostics.Process process)
    {
        try { return process.StartTime; }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
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

    private static void Observe(ConditionState state, FileStamp stamp)
    {
        state.Exists = stamp.Exists;
        state.LengthBytes = stamp.Exists ? stamp.Length : null;
        state.LastWriteTime = stamp.Exists ? new DateTimeOffset(stamp.LastWriteTicks, TimeSpan.Zero) : null;
    }

    /// <summary>
    /// What the file looks like now, through the contract rather than through a fresh
    /// <c>FileInfo</c>: the entry answers whether it is there and how long it is, and the write time
    /// is asked for only for an entry that exists.
    /// </summary>
    private async Task<FileStamp> SnapshotAsync(string path, CancellationToken cancellationToken)
    {
        var entry = await _files.GetEntryAsync(path, cancellationToken);
        if (entry is null || entry.IsDirectory)
        {
            return default;
        }

        var written = await _files.GetLastWriteTimeAsync(path, cancellationToken);
        return new FileStamp(true, entry.Length, written.UtcTicks);
    }

    private static void Wake(SemaphoreSlim signal)
    {
        try { signal.Release(); }
        catch (SemaphoreFullException) { }
        catch (ObjectDisposedException) { }
    }

    private static async Task WakeOnCompletionAsync(Task<ProcessResult> completion, SemaphoreSlim signal)
    {
        try { await completion; }
        // Evaluation propagates failures; disposal also observes cancelled/losing subscriptions.
        catch (Exception) { }
        finally { Wake(signal); }
    }

    private static async Task WakeOnExitAsync(Task exit, SemaphoreSlim signal)
    {
        try { await exit; }
        catch (Exception) { }
        finally { Wake(signal); }
    }

    private readonly record struct FileStamp(bool Exists, long Length, long LastWriteTicks);

    private enum TriggerKind
    {
        Delay, FileExists, FileMissing, FileChanged, FileContains, FileIdle, ProcessExit, ProcessCompleted,
        ProcessOutputContains, ProcessCpuBelow, ProcessCpuAbove, ProcessMemoryBelow, ProcessMemoryAbove
    }

    /// <summary>
    /// One platform watcher per watched directory and depth, however many conditions look into it: a
    /// condition costs a subscription, not a watcher with its own buffer and its own overflow risk.
    /// A subscriber receives every entry the directory reports and decides whether it is its own
    /// path, so the filter stays exact without a per-condition watcher filter.
    /// </summary>
    private sealed class FileWatchRegistry(IFileWatcherFactory factory) : IDisposable
    {
        private readonly Dictionary<(string Directory, bool Recursive), WatchedDirectory> _watched = new();

        /// <summary>
        /// Subscribes to the watcher for <paramref name="directory"/>, creating it when this is the
        /// first condition that looks there. Either everything this condition needs is in place or
        /// nothing is: a condition that fails later must not leave a watcher behind.
        /// </summary>
        public IDisposable Add(string directory, bool recursive, string path, Action<string> onEvent, Action onOverflow)
        {
            if (!_watched.TryGetValue((directory, recursive), out var watched))
            {
                watched = new WatchedDirectory(factory, directory, recursive);
                var subscription = watched.Add(path, onEvent, onOverflow);
                try
                {
                    watched.Start();
                }
                catch
                {
                    subscription.Dispose();
                    throw;
                }
                _watched.Add((directory, recursive), watched);
                return subscription;
            }
            return watched.Add(path, onEvent, onOverflow);
        }

        public void Dispose()
        {
            foreach (var watched in _watched.Values.ToArray()) watched.Dispose();
            _watched.Clear();
        }

        private sealed class WatchedDirectory : IDisposable
        {
            private readonly object _gate = new();
            private readonly Dictionary<string, List<Subscription>> _subscriptions = new(StringComparer.Ordinal);
            private readonly IFileWatcher _watcher;
            private int _count;
            private bool _disposed;

            public WatchedDirectory(IFileWatcherFactory factory, string directory, bool recursive)
            {
                _watcher = factory.Create(directory, recursive);
                _watcher.Changed += Dispatch;
                _watcher.Renamed += (oldPath, newPath) => { Dispatch(oldPath); Dispatch(newPath); };
                _watcher.Faulted += Overflow;
            }

            public void Start() => _watcher.Start();

            public Subscription Add(string path, Action<string> onEvent, Action onOverflow)
            {
                var subscription = new Subscription(this, path, onEvent, onOverflow);
                lock (_gate)
                {
                    if (!_subscriptions.TryGetValue(path, out var list))
                        _subscriptions.Add(path, list = []);
                    list.Add(subscription);
                    _count++;
                }
                return subscription;
            }

            public void Remove(string path, Subscription subscription)
            {
                var empty = false;
                lock (_gate)
                {
                    if (_subscriptions.TryGetValue(path, out var list) && list.Remove(subscription))
                        _count--;
                    empty = _count == 0;
                }
                if (empty) Dispose();
            }

            private void Dispatch(string candidate)
            {
                Subscription[] targets;
                lock (_gate) targets = _subscriptions.Values.SelectMany(list => list).ToArray();
                foreach (var target in targets) target.Event(candidate);
            }

            private void Overflow()
            {
                Subscription[] targets;
                lock (_gate) targets = _subscriptions.Values.SelectMany(list => list).ToArray();
                foreach (var target in targets) target.Overflow();
            }

            public void Dispose()
            {
                lock (_gate)
                {
                    if (_disposed) return;
                    _disposed = true;
                    _subscriptions.Clear();
                    _count = 0;
                }
                _watcher.Dispose();
            }
        }

        private sealed class Subscription(WatchedDirectory owner, string path, Action<string> onEvent, Action onOverflow) : IDisposable
        {
            private int _disposed;

            public void Event(string candidate) => onEvent(candidate);

            public void Overflow() => onOverflow();

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Remove(path, this);
            }
        }
    }

    private sealed class ConditionState(TriggerCondition condition, TriggerKind kind, int index) : IDisposable
    {
        public TriggerCondition Condition { get; } = condition;
        public TriggerKind Kind { get; } = kind;
        public int Index { get; } = index;
        public string? Path { get; set; }
        public IDisposable? Watch { get; set; }
        public System.Diagnostics.Process? Process { get; set; }
        public DateTimeOffset? BoundAt { get; set; }
        public Task<ProcessResult>? Completion { get; set; }
        public Task? CompletionObserver { get; set; }
        public Task? Exit { get; set; }
        public Task? ExitObserver { get; set; }
        public FileStamp Baseline { get; set; }
        public FileStamp LastFile { get; set; }
        public int FileEvents;
        public bool SawFileChange { get; set; }
        public long LastChangeMs { get; set; }
        public long? EligibleSinceMs { get; set; }
        public TimeSpan? PreviousCpuTime { get; set; }
        public long PreviousSampleMs { get; set; }
        public double? CpuPercent { get; set; }
        public long? MemoryBytes { get; set; }
        public bool? Exists { get; set; }
        public long? LengthBytes { get; set; }
        public DateTimeOffset? LastWriteTime { get; set; }
        public bool? ProcessRunning { get; set; }
        public string? ProcessStatus { get; set; }
        public bool? Matched { get; set; }
        public long? SearchedBytes { get; set; }
        public bool? OlderTextUnsearched { get; set; }
        public long StdoutOffset { get; set; }
        public long StderrOffset { get; set; }

        /// <summary>
        /// The end of the managed output read so far, kept only as far as a pattern could straddle it.
        /// </summary>
        public string? OutputTail { get; set; }

        public string? Error { get; set; }

        public void Dispose()
        {
            Watch?.Dispose();
            Process?.Dispose();
        }
    }
}
