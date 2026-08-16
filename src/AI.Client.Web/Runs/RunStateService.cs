namespace AI.Client.Web.Runs;

using Contracts.Runs;

public readonly record struct RunKey(Guid ChatId, Guid BranchId);

public sealed class RunStateService
{
    private readonly Dictionary<RunKey, ChatRunSnapshot> _runs = [];
    public IReadOnlyDictionary<RunKey, ChatRunSnapshot> Runs => _runs;

    public void RemoveMissing(IReadOnlyList<ChatRunSnapshot> snapshot)
    {
        var keys = snapshot.Select(run => new RunKey(run.ChatId, run.BranchId)).ToHashSet();
        foreach (var key in _runs.Keys.Where(key => !keys.Contains(key)).ToArray()) _runs.Remove(key);
    }

    public void Store(ChatRunSnapshot run)
    {
        var key = new RunKey(run.ChatId, run.BranchId);
        if (!_runs.TryGetValue(key, out var previous) || run.Revision > previous.Revision
            || run.Revision == previous.Revision && run.ChatRevision >= previous.ChatRevision)
            _runs[key] = run;
    }
}
