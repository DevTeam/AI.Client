namespace AI.Application.Tools;

using System.Collections.Concurrent;
using System.Text.Json;
using AI.Contracts.Tools;

/// <summary>Shared execution slots and run-local handles. No work or results survive their parent run.</summary>
public sealed class SubtaskRuns : ISubtaskRuns, IAsyncDisposable
{
    private const int MaxConcurrentRuns = 32;
    private const int MaxTasksPerRun = 256;
    private readonly SemaphoreSlim _slots = new(MaxConcurrentRuns);
    private readonly ConcurrentDictionary<Owner, Scope> _owners = new();

    public IAsyncDisposable Begin(ToolRunContext owner, CancellationToken cancellationToken)
    {
        var key = Owner.Of(owner);
        var scope = new Scope(this, key, cancellationToken);
        if (_owners.TryAdd(key, scope)) return scope;
        scope.Cancellation.Dispose();
        throw new InvalidOperationException("This branch already has an active subtask scope.");
    }

    public int NextConnection(ToolRunContext owner) => Interlocked.Increment(ref Get(owner).Connection) - 1;

    public SubtaskSnapshot Start(ToolRunContext owner, string task,
        Func<Action<string>, CancellationToken, Task<SubtaskCompletion>> execute)
    {
        var scope = Get(owner);
        lock (scope.Gate)
        {
            if (scope.Ended || scope.Cancellation.IsCancellationRequested)
                throw new InvalidOperationException("The parent run has ended.");
            if (scope.Jobs.Count >= MaxTasksPerRun)
                throw new InvalidOperationException("This run has reached its subtask resource limit.");
            var job = new Job(Guid.CreateVersion7().ToString("N"), task, scope.Cancellation.Token);
            scope.Jobs.Add(job.Id, job);
            job.Work = Task.Run(() => ExecuteAsync(job, owner.IsSubtask, execute), CancellationToken.None);
            return Snapshot(job, false);
        }
    }

    private async Task ExecuteAsync(Job job, bool nested,
        Func<Action<string>, CancellationToken, Task<SubtaskCompletion>> execute)
    {
        var entered = false;
        try
        {
            entered = nested ? await _slots.WaitAsync(0, job.Cancellation.Token)
                : await EnterAsync(job.Cancellation.Token);
            if (!entered) throw new InvalidOperationException("No execution slot is available for nested work. Continue directly.");
            lock (job.Gate) job.Status = "running";
            var completion = await execute(activity => { lock (job.Gate) job.Activity = activity; }, job.Cancellation.Token);
            lock (job.Gate)
            {
                job.Completion = completion;
                job.Status = job.Cancellation.IsCancellationRequested ? "cancelled"
                    : completion.Outcome.Error is null ? "completed" : "failed";
                job.Activity = null;
            }
        }
        catch (Exception error)
        {
            lock (job.Gate)
            {
                var cancelled = job.Cancellation.IsCancellationRequested;
                var message = cancelled ? "The subtask was cancelled." : error.Message;
                job.Status = cancelled ? "cancelled" : "failed";
                job.Activity = null;
                job.Completion = new(new(job.Task, "", "", 0, 0, 0, false, message, [], [], [message]), []);
            }
        }
        finally { if (entered) _slots.Release(); }
    }

    private async Task<bool> EnterAsync(CancellationToken token)
    {
        await _slots.WaitAsync(token);
        return true;
    }

    public async Task<SubtaskWaitResult> WaitAsync(ToolRunContext owner, IReadOnlyList<string> taskIds, bool all,
        int waitTimeoutMs, IProgress<SubtaskProgress>? progress, CancellationToken cancellationToken)
    {
        if (waitTimeoutMs is < 0 or > 60000) throw new ArgumentOutOfRangeException(nameof(waitTimeoutMs));
        var jobs = Resolve(owner, taskIds);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshots = jobs.Select(job => Snapshot(job, true)).ToArray();
            var done = snapshots.Count(item => Terminal(item.Status));
            progress?.Report(new(done, jobs.Length, string.Join(" · ", snapshots.Select(item =>
                $"{item.TaskId[..8]}: {item.Activity ?? item.Status}"))));
            var settled = all ? done == jobs.Length : done > 0;
            if (settled || clock.ElapsedMilliseconds >= waitTimeoutMs)
                return new(snapshots, Next: done == jobs.Length
                    ? "Integrate these results. Trust passed local checks and verify the combined result; inspect errors and unresolved items."
                    : "Some tasks are still running. Continue independent work or wait on their taskIds; do not restart them.",
                    TimedOut: !settled && waitTimeoutMs > 0);
            var remaining = Math.Min(1000, waitTimeoutMs - (int)clock.ElapsedMilliseconds);
            if (remaining <= 0) continue;
            var active = jobs.Where(job => !job.Work.IsCompleted).Select(job => job.Work).ToArray();
            // Already-finished handles are evaluated above. Waiting on only active tasks avoids a busy loop.
            if (active.Length == 0) continue;
            var completion = all ? Task.WhenAll(active) : Task.WhenAny(active);
            await Task.WhenAny(completion, Task.Delay(remaining, cancellationToken));
        }
    }

    public async Task<SubtaskWaitResult> StopAsync(ToolRunContext owner, IReadOnlyList<string> taskIds, CancellationToken cancellationToken)
    {
        var jobs = Resolve(owner, taskIds); // Validate every handle before cancelling any task.
        foreach (var job in jobs) await job.Cancellation.CancelAsync();
        await Task.WhenAll(jobs.Select(job => job.Work)).WaitAsync(cancellationToken);
        return new(jobs.Select(job => Snapshot(job, true)).ToArray(), Next: "Inspect partial results and unresolved items before continuing.");
    }

    public IReadOnlyList<SubtaskSnapshot> Outstanding(ToolRunContext owner)
    {
        if (!_owners.TryGetValue(Owner.Of(owner), out var scope)) return [];
        lock (scope.Gate) return scope.Jobs.Values.Where(job => !job.Acknowledged).Select(job => Snapshot(job, false)).ToArray();
    }

    public IReadOnlyList<SubtaskTranscriptEntry> Transcript(ToolRunContext owner, IReadOnlyList<string> taskIds) =>
        Resolve(owner, taskIds).SelectMany(job => { lock (job.Gate) return job.Completion?.Transcript.ToArray() ?? []; }).ToArray();

    public void Acknowledge(ToolRunContext owner, JsonElement? result)
    {
        if (result is not { ValueKind: JsonValueKind.Object } root || !root.TryGetProperty("tasks", out var tasks)
            || tasks.ValueKind != JsonValueKind.Array || !_owners.TryGetValue(Owner.Of(owner), out var scope)) return;
        lock (scope.Gate)
            foreach (var task in tasks.EnumerateArray())
                if (task.TryGetProperty("taskId", out var id) && id.ValueKind == JsonValueKind.String
                    && task.TryGetProperty("result", out var outcome) && outcome.ValueKind == JsonValueKind.Object
                    && scope.Jobs.TryGetValue(id.GetString()!, out var job) && Terminal(Snapshot(job, false).Status))
                    job.Acknowledged = true;
    }

    public async Task StopAllAsync(ToolRunContext owner)
    {
        if (!_owners.TryGetValue(Owner.Of(owner), out var scope)) return;
        Job[] jobs;
        lock (scope.Gate) jobs = scope.Jobs.Values.ToArray();
        foreach (var job in jobs) await job.Cancellation.CancelAsync();
        await Task.WhenAll(jobs.Select(job => job.Work));
    }

    private Scope Get(ToolRunContext owner) => _owners.TryGetValue(Owner.Of(owner), out var scope) ? scope
        : throw new InvalidOperationException("The parent run is no longer active; its subtask handles have expired.");

    private Job[] Resolve(ToolRunContext owner, IReadOnlyList<string> ids)
    {
        if (ids.Count == 0) throw new ArgumentException("Provide at least one taskId.", nameof(ids));
        var scope = Get(owner);
        lock (scope.Gate) return ids.Distinct(StringComparer.Ordinal).Select(id =>
            scope.Jobs.TryGetValue(id, out var job) ? job
                : throw new ArgumentException("A taskId is unknown or belongs to another run.", nameof(ids))).ToArray();
    }

    private static SubtaskSnapshot Snapshot(Job job, bool result)
    {
        lock (job.Gate) return new(job.Id, job.Task, job.Status,
            result && Terminal(job.Status) ? job.Completion?.Outcome : null, job.Activity);
    }

    private static bool Terminal(string status) => status is "completed" or "failed" or "cancelled";

    public async ValueTask DisposeAsync()
    {
        foreach (var scope in _owners.Values) await scope.DisposeAsync();
        _slots.Dispose();
    }

    private readonly record struct Owner(Guid Project, Guid Chat, Guid Branch)
    {
        public static Owner Of(ToolRunContext run) => new(run.ProjectId, run.ChatId, run.BranchId);
    }

    private sealed class Scope(SubtaskRuns owner, Owner key, CancellationToken token) : IAsyncDisposable
    {
        public Lock Gate { get; } = new();
        public CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(token);
        public Dictionary<string, Job> Jobs { get; } = new(StringComparer.Ordinal);
        public int Connection;
        public bool Ended;

        public async ValueTask DisposeAsync()
        {
            Job[] jobs;
            lock (Gate)
            {
                if (Ended) return;
                Ended = true;
                jobs = Jobs.Values.ToArray();
            }
            await Cancellation.CancelAsync();
            await Task.WhenAll(jobs.Select(job => job.Work));
            owner._owners.TryRemove(new KeyValuePair<Owner, Scope>(key, this));
            foreach (var job in jobs) job.Cancellation.Dispose();
            Cancellation.Dispose();
        }
    }

    private sealed class Job(string id, string task, CancellationToken token)
    {
        public Lock Gate { get; } = new();
        public string Id { get; } = id;
        public string Task { get; } = task;
        public CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(token);
        public string Status = "queued";
        public string? Activity;
        public bool Acknowledged;
        public SubtaskCompletion? Completion;
        public Task Work = System.Threading.Tasks.Task.CompletedTask;
    }
}
