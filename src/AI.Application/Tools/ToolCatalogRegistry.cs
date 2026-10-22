namespace AI.Application.Tools;

using System.Collections.Concurrent;
using System.Text.Json;
using Search;

public sealed class ToolCatalogRegistry : IToolCatalogRegistry
{
    /// <summary>How many of a search's best matches are prioritized for the next model step.</summary>
    public const int MaxPinned = 3;

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

    public IReadOnlySet<string> ConsumePinned(ToolRunContext run)
    {
        if (!_entries.TryGetValue(Key.Of(run), out var entry)) return new HashSet<string>(StringComparer.Ordinal);
        var names = entry.Pinned.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var name in names) entry.Pinned.TryRemove(name, out _);
        return names;
    }

    public ToolCatalogSearch SearchAndPin(ToolRunContext run, string query, int limit, string? server = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (!SearchText.IsEnglish(query)) throw new ArgumentException(SearchText.EnglishOnly, nameof(query));
        if (!_entries.TryGetValue(Key.Of(run), out var entry)) return new([], 0);
        var scoped = entry.Tools.Where(tool => server is null || OnServer(tool, server.Trim())).ToArray();
        var name = query.Trim();
        var exact = scoped.FirstOrDefault(tool => tool.ModelDefinition.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            || tool.ModelDefinition.Name.EndsWith("__" + name, StringComparison.OrdinalIgnoreCase));
        var terms = SearchText.Terms(query);
        AgentTool[] ranked = exact is not null
            ? [exact]
            : scoped.Select(tool => new { Tool = tool, Score = Score(tool, terms) })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Tool.ModelDefinition.Name, StringComparer.Ordinal)
                .Select(item => item.Tool)
                .ToArray();
        var matches = ranked.Take(Math.Clamp(limit, 1, 8)).ToArray();
        // All searches in this step contribute to the next selection. Its consumer releases the
        // priorities; actual protocol calls are protected separately by the context policy. Only the
        // best few are pinned, because pins narrow the rest of the selection.
        var pinned = matches.Take(MaxPinned).Select(tool => tool.ModelDefinition.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var tool in pinned) entry.Pinned.TryAdd(tool, 0);
        return new(matches.Select(tool => new ToolCatalogMatch(tool.ModelDefinition.Name,
            Short(tool.ModelDefinition.Description), tool.ServerId.ToString(), pinned.Contains(tool.ModelDefinition.Name))).ToArray(),
            ranked.Length - matches.Length);
    }

    /// <summary>
    /// A whole word of the name counts most, a word the name begins with less, and the description
    /// and parameters least, so a tool is found by what it is called before what it mentions.
    /// </summary>
    private static int Score(AgentTool tool, IReadOnlyList<IReadOnlySet<string>> terms)
    {
        var names = SearchText.Index([tool.ModelDefinition.Name]);
        var description = SearchText.Index([tool.ModelDefinition.Description]);
        var parameters = SearchText.Index(Parameters(tool.ModelDefinition.InputSchema));
        return terms.Sum(term => SearchText.Matches(term, names) ? 12
            : SearchText.BeginsAWordOf(term, tool.ModelDefinition.Name) ? 8
            : SearchText.Matches(term, description) ? 4
            : SearchText.Matches(term, parameters) ? 2
            : 0);
    }

    private static IEnumerable<string?> Parameters(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object) yield break;
        foreach (var property in properties.EnumerateObject())
        {
            yield return property.Name;
            if (property.Value.ValueKind == JsonValueKind.Object && property.Value.TryGetProperty("description", out var text)
                && text.ValueKind == JsonValueKind.String) yield return text.GetString();
        }
    }

    /// <summary>A server is named as in the tools' prefix (<c>github</c> for <c>mcp_github__…</c>) or by its id.</summary>
    private static bool OnServer(AgentTool tool, string server) =>
        tool.ModelDefinition.Name.StartsWith($"mcp_{server}__", StringComparison.OrdinalIgnoreCase)
        || tool.ModelDefinition.Name.StartsWith(server + "__", StringComparison.OrdinalIgnoreCase)
        || tool.ServerId.ToString().Equals(server, StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> Pin(ToolRunContext run, IEnumerable<string> names)
    {
        if (!_entries.TryGetValue(Key.Of(run), out var entry)) return [];
        var wanted = names.ToHashSet(StringComparer.Ordinal);
        var pinned = entry.Tools
            .Select(tool => tool.ModelDefinition.Name)
            .Where(name => wanted.Contains(name) || wanted.Any(bare => name.EndsWith("__" + bare, StringComparison.Ordinal)))
            .ToArray();
        foreach (var name in pinned) entry.Pinned.TryAdd(name, 0);
        return pinned;
    }

    public bool TryRecordPlaybook(ToolRunContext run, string skillId, string arguments) =>
        !_entries.TryGetValue(Key.Of(run), out var entry) || entry.Playbooks.TryAdd((skillId, arguments), 0);

    private static string Short(string value) => value.Length <= 240 ? value : value[..240] + "…";

    private readonly record struct Key(Guid ProjectId, Guid ChatId, Guid BranchId)
    {
        public static Key Of(ToolRunContext run) => new(run.ProjectId, run.ChatId, run.BranchId);
    }

    private sealed class Entry
    {
        public IReadOnlyList<AgentTool> Tools { get; set; } = [];
        public ConcurrentDictionary<string, byte> Pinned { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<(string SkillId, string Arguments), byte> Playbooks { get; } = new();
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
