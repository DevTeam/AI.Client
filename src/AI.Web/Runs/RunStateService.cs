namespace AI.Web.Runs;

using Contracts.Runs;

public readonly record struct RunKey(Guid ChatId, Guid BranchId);

public sealed class RunStateService : IRunStateService
{
    private readonly Dictionary<RunKey, ChatRunSnapshot> _runs = [];
    private readonly Dictionary<RunKey, LlmGeneratingTracker> _llmGenerating = [];
    public IReadOnlyDictionary<RunKey, ChatRunSnapshot> Runs => _runs;

    public void RemoveMissing(IReadOnlyList<ChatRunSnapshot> snapshot)
    {
        var keys = snapshot.Select(run => new RunKey(run.ChatId, run.BranchId)).ToHashSet();
        foreach (var key in _runs.Keys.Where(key => !keys.Contains(key)).ToArray())
        {
            _runs.Remove(key);
            _llmGenerating.Remove(key);
        }
    }

    public void Remove(IReadOnlyList<ChatRunKey> keys)
    {
        foreach (var item in keys)
        {
            var key = new RunKey(item.ChatId, item.BranchId);
            _runs.Remove(key);
            _llmGenerating.Remove(key);
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

    public void Store(ChatRunSnapshot run)
    {
        var key = new RunKey(run.ChatId, run.BranchId);
        if (!_runs.TryGetValue(key, out var previous) || run.Revision > previous.Revision
            || run.Revision == previous.Revision && run.ChatRevision >= previous.ChatRevision)
        {
            if (!_llmGenerating.TryGetValue(key, out var tracker)) tracker = new LlmGeneratingTracker();
            tracker.Apply(run);
            _llmGenerating[key] = tracker;
            _runs[key] = run;
        }
    }

    // Elapsed time spent with the LLM itself producing the response — excludes time spent
    // waiting on a tool approval decision or a tool actually running, since neither is the LLM
    // "generating".
    public TimeSpan? GetLlmGeneratingElapsed(RunKey key) =>
        _llmGenerating.TryGetValue(key, out var tracker) ? tracker.GetElapsed() : null;

    private sealed class LlmGeneratingTracker
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

            var isLlmGenerating = run.PendingApproval is null && run.PendingPrompt is null
                && run.ActiveTools is not { Count: > 0 };
            var now = DateTimeOffset.UtcNow;
            if (isLlmGenerating)
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
