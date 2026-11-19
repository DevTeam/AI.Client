namespace AI.Application.Tools;

using System.Collections.Concurrent;
using Projects;

/// <summary>
/// The tools a branch's last request carried, kept between its turns. A turn that starts from them
/// sends the same tool definitions as the turn before, so the provider can reuse its cached prefix;
/// a turn that ranked the tools afresh for its new message changed the very start of the request
/// and missed the cache on the whole history.
/// </summary>
public interface IToolSetMemory
{
    /// <summary>The remembered tools still offered in <paramref name="offered"/>, in the order they were sent; null when there are none.</summary>
    IReadOnlyList<AgentTool>? Recall(Guid chatId, Guid branchId, string model, IReadOnlyList<AgentTool> offered);

    void Remember(Guid chatId, Guid branchId, string model, IReadOnlyList<AgentTool> sent);
}

public sealed class ToolSetMemory(IClock clock) : IToolSetMemory
{
    /// <summary>
    /// Past this, the provider's cache has most likely let the prefix go, and a set chosen for an old
    /// request is no better a start than one ranked for the new message.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    /// <summary>Branches remembered at once; the oldest go first, and a forgotten one is ranked afresh.</summary>
    private const int MaximumKeys = 512;

    private readonly ConcurrentDictionary<(Guid ChatId, Guid BranchId, string Model), (string[] Names, DateTimeOffset At)> _sets = new();

    public IReadOnlyList<AgentTool>? Recall(Guid chatId, Guid branchId, string model, IReadOnlyList<AgentTool> offered)
    {
        ArgumentNullException.ThrowIfNull(offered);
        if (!_sets.TryGetValue((chatId, branchId, model), out var entry) || clock.UtcNow - entry.At > Lifetime) return null;
        var byName = offered.GroupBy(tool => tool.ModelDefinition.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var tools = entry.Names.Where(byName.ContainsKey).Select(name => byName[name]).ToArray();
        return tools.Length == 0 ? null : tools;
    }

    public void Remember(Guid chatId, Guid branchId, string model, IReadOnlyList<AgentTool> sent)
    {
        ArgumentNullException.ThrowIfNull(sent);
        var now = clock.UtcNow;
        _sets[(chatId, branchId, model)] = (sent.Select(tool => tool.ModelDefinition.Name).ToArray(), now);
        if (_sets.Count <= MaximumKeys) return;
        foreach (var stale in _sets.OrderBy(item => item.Value.At).Take(_sets.Count - MaximumKeys).ToArray())
            _sets.TryRemove(stale.Key, out _);
    }
}
