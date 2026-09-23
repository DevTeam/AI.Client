namespace AI.Client.Application.Tools;

using System.Collections.Concurrent;

public sealed class ToolCatalogRegistry : IToolCatalogRegistry
{
    private readonly ConcurrentDictionary<Key, Entry> _entries = new();

    public IDisposable Begin(ToolRunContext run)
    {
        var key = Key.Of(run);
        _entries[key] = new Entry();
        return new Scope(() => _entries.TryRemove(key, out _));
    }

    public void Update(ToolRunContext run, IReadOnlyList<AgentTool> tools)
    {
        if (_entries.TryGetValue(Key.Of(run), out var entry)) entry.Tools = tools;
    }

    public IReadOnlySet<string> GetPinned(ToolRunContext run) =>
        _entries.TryGetValue(Key.Of(run), out var entry)
            ? entry.Pinned.Keys.ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlyList<ToolCatalogMatch> SearchAndPin(ToolRunContext run, string query, int limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (!_entries.TryGetValue(Key.Of(run), out var entry)) return [];
        var words = query.Split([' ', '\t', '\r', '\n', '_', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matches = entry.Tools.Select(tool => new
            {
                Tool = tool,
                Score = words.Count(word => tool.ModelDefinition.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
                    || tool.ModelDefinition.Description.Contains(word, StringComparison.OrdinalIgnoreCase))
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Tool.ModelDefinition.Name, StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, 8))
            .Select(item => item.Tool)
            .ToArray();
        foreach (var tool in matches) entry.Pinned.TryAdd(tool.ModelDefinition.Name, 0);
        return matches.Select(tool => new ToolCatalogMatch(tool.ModelDefinition.Name,
            Short(tool.ModelDefinition.Description), tool.ServerId.ToString())).ToArray();
    }

    private static string Short(string value) => value.Length <= 240 ? value : value[..240] + "…";

    private readonly record struct Key(Guid ProjectId, Guid ChatId, Guid BranchId)
    {
        public static Key Of(ToolRunContext run) => new(run.ProjectId, run.ChatId, run.BranchId);
    }

    private sealed class Entry
    {
        public IReadOnlyList<AgentTool> Tools { get; set; } = [];
        public ConcurrentDictionary<string, byte> Pinned { get; } = new(StringComparer.Ordinal);
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
