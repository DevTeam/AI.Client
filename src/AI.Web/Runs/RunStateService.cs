namespace AI.Web.Runs;

using Contracts.Runs;

public readonly record struct RunKey(Guid ChatId, Guid BranchId);

public sealed class RunStateService : IRunStateService
{
    private readonly Dictionary<RunKey, ChatRunSnapshot> _runs = [];
    private readonly Dictionary<RunKey, WorkingTracker> _working = [];
    public IReadOnlyDictionary<RunKey, ChatRunSnapshot> Runs => _runs;

    public void RemoveMissing(IReadOnlyList<ChatRunSnapshot> snapshot)
    {
        var keys = snapshot.Select(run => new RunKey(run.ChatId, run.BranchId)).ToHashSet();
        foreach (var key in _runs.Keys.Where(key => !keys.Contains(key)).ToArray())
        {
            _runs.Remove(key);
            _working.Remove(key);
        }
    }

    public void Remove(IReadOnlyList<ChatRunKey> keys)
    {
        foreach (var item in keys)
        {
            var key = new RunKey(item.ChatId, item.BranchId);
            _runs.Remove(key);
            _working.Remove(key);
        }
    }

    public void AppendStreaming(IReadOnlyList<ChatRunStreamingAppend> appends)
    {
        foreach (var append in appends)
        {
            var key = new RunKey(append.ChatId, append.BranchId);
            if (!_runs.TryGetValue(key, out var current) || append.Revision <= current.Revision) continue;
            Store(current with
            {
                StreamingContent = current.StreamingContent + append.Content,
                Revision = append.Revision
            });
        }
    }

    public void AppendDraft(IReadOnlyList<ChatRunDraftAppend> appends)
    {
        foreach (var append in appends)
        {
            var key = new RunKey(append.ChatId, append.BranchId);
            // Anything else means a publication in between was not seen; the next whole snapshot
            // brings the draft as it is.
            if (!_runs.TryGetValue(key, out var current) || append.Revision != current.Revision
                || (current.DraftContent?.Length ?? 0) != append.BaseLength) continue;
            _runs[key] = current with { DraftContent = (current.DraftContent ?? string.Empty) + append.Content };
        }
    }

    public void Store(ChatRunSnapshot run)
    {
        var key = new RunKey(run.ChatId, run.BranchId);
        if (!_runs.TryGetValue(key, out var previous) || run.Revision > previous.Revision
            || run.Revision == previous.Revision && run.ChatRevision >= previous.ChatRevision)
        {
            if (!_working.TryGetValue(key, out var tracker)) tracker = new WorkingTracker();
            tracker.Apply(run);
            _working[key] = tracker;
            _runs[key] = run;
        }
    }

    // Elapsed time the run spent working — the model generating and its tools running — without
    // the time it waited on the person for a tool approval or an answer. A running tool is work:
    // stopping the clock for it froze "Working for …" through every long call.
    public TimeSpan? GetWorkingElapsed(RunKey key) =>
        _working.TryGetValue(key, out var tracker) ? tracker.GetElapsed() : null;

    private sealed class WorkingTracker
    {
        private TimeSpan _accumulated;
        private DateTimeOffset? _segmentStart;
        private bool _everStarted;

        public void Apply(ChatRunSnapshot run)
        {
            if (run.Status != ChatRunStatus.Generating)
            {
                _accumulated = TimeSpan.Zero;
                _segmentStart = null;
                _everStarted = false;
                return;
            }

            var isWorking = run.PendingApproval is null && run.PendingPrompt is null;
            var now = DateTimeOffset.UtcNow;
            if (isWorking)
            {
                _segmentStart ??= now;
                _everStarted = true;
            }
            else if (_segmentStart is { } start)
            {
                _accumulated += now - start;
                _segmentStart = null;
            }
        }

        public TimeSpan? GetElapsed()
        {
            if (!_everStarted) return null;
            return _segmentStart is { } start ? _accumulated + (DateTimeOffset.UtcNow - start) : _accumulated;
        }
    }
}
