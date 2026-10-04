namespace AI.Web.Widgets;

using System.Text.Json;
using AI.Contracts.Chat;
using AI.Contracts.Chats;
using AI.Contracts.Runs;
using AI.Contracts.Tools;

/// <summary>One source the assistant read on the visible branch.</summary>
/// <param name="Display">The path or URL the call asked for, with backslashes normalised to slashes.</param>
/// <param name="Occurrences">How many times the same source was read in scope.</param>
public sealed record KnowledgeSource(string Display, int Occurrences);

/// <summary>Sources grouped by the tool that read them.</summary>
/// <param name="Server">Where the tool came from: Built-in, App, or a third-party server name.</param>
/// <param name="ToolName">The bare tool name, like <c>read_text_file</c>.</param>
/// <param name="ToolLabel">A short human label: "Read file", "Fetch page", ...</param>
/// <param name="Calls">Total read calls in scope for this tool.</param>
/// <param name="Recent">The newest sources, most recent first, deduplicated.</param>
public sealed record ChatKnowledgeSourceGroup(
    string Server,
    string ToolName,
    string ToolLabel,
    int Calls,
    IReadOnlyList<KnowledgeSource> Recent);

/// <summary>All sources of knowledge on the visible branch or last turn.</summary>
/// <param name="Groups">One entry per (server, tool) pair, ordered by calls then label.</param>
/// <param name="TotalReads">How many read calls happened in scope.</param>
/// <param name="TurnsWithReads">How many of the scope's turns contained at least one read.</param>
/// <param name="IsRunning">True while the last turn is still going.</param>
public sealed record ChatKnowledgeStatistics(
    IReadOnlyList<ChatKnowledgeSourceGroup> Groups,
    int TotalReads,
    int TurnsWithReads,
    bool IsRunning)
{
    public static ChatKnowledgeStatistics Empty { get; } = new([], 0, 0, false);
}

/// <summary>
/// Builds a <see cref="ChatKnowledgeStatistics"/> for the visible branch and the chosen scope.
/// The set of read tools is fixed and comes from the built-in presentation adapters; anything
/// else — write, edit, delete, <c>process_run</c>, unknown third-party tools — is never a source
/// of knowledge here, even when the assistant happens to read its result.
/// </summary>
public interface IChatKnowledgeStatisticsCalculator
{
    ChatKnowledgeStatistics Calculate(
        IReadOnlyList<ChatMessageView> branch,
        ChatRunSnapshot? run,
        ChatWidgetScope scope);
}

public sealed class ChatKnowledgeStatisticsCalculator : IChatKnowledgeStatisticsCalculator
{
    // The names mirror FileToolPresentationAdapter.Names (read-only entries) and
    // WebToolPresentationAdapter.Names. The widget counts them as sources of knowledge, nothing
    // else.
    private static readonly HashSet<string> ReadTools = new(StringComparer.Ordinal)
    {
        "read_text_file", "read_multiple_files",
        "list_directory", "directory_tree",
        "search_files", "grep_files",
        "get_file_info", "list_allowed_directories",
        "zip_list", "zip_read",
        "fetch"
    };

    public ChatKnowledgeStatistics Calculate(
        IReadOnlyList<ChatMessageView> branch,
        ChatRunSnapshot? run,
        ChatWidgetScope scope)
    {
        if (branch.Count == 0) return ChatKnowledgeStatistics.Empty;
        var turns = SplitTurns(branch);
        if (turns.Count == 0) return ChatKnowledgeStatistics.Empty;

        // Same rule ChatToolStatisticsCalculator uses: a snapshot for a different branch, or an
        // earlier turn, must not leak into this scope.
        var isRunning = run is { Status: ChatRunStatus.Generating or ChatRunStatus.Paused, ActiveMessageId: { } runActive }
            && turns[^1].Any(message => message.Id == runActive);

        var scopedTurns = scope == ChatWidgetScope.LastTurn && turns.Count > 1 ? turns[^1..] : turns;

        var groups = new Dictionary<string, GroupAccumulator>(StringComparer.Ordinal);
        var totalReads = 0;
        var turnsWithReads = 0;

        for (var index = 0; index < scopedTurns.Count; index++)
        {
            var turn = scopedTurns[index];
            var live = isRunning && index == scopedTurns.Count - 1;
            var activeReads = live ? ActiveReads(run!) : [];
            var hadRead = false;

            foreach (var message in turn)
            {
                if (message.Role != "Assistant") continue;
                foreach (var call in message.ToolCalls ?? [])
                {
                    if (!IsRead(call.Name)) continue;
                    hadRead = true;
                    var readCount = 0;
                    foreach (var source in ExtractSources(call.Name, call.Arguments))
                    {
                        Add(groups, call.Name, source);
                        readCount++;
                    }
                    // A read_multiple_files call with no parseable paths still counts as one read
                    // attempt; otherwise a malformed batch would silently disappear from the totals.
                    if (readCount == 0) readCount = 1;
                    totalReads += readCount;
                }
            }

            foreach (var liveCall in activeReads)
            {
                if (!IsRead(liveCall.Name)) continue;
                hadRead = true;
                foreach (var source in ExtractSources(liveCall.Name, liveCall.Arguments))
                    Add(groups, liveCall.Name, source);
                totalReads++;
            }

            if (hadRead) turnsWithReads++;
        }

        var result = groups.Values
            .OrderByDescending(item => item.Calls)
            .ThenBy(item => item.ToolLabel, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.ToGroup())
            .ToArray();

        return new ChatKnowledgeStatistics(result, totalReads, turnsWithReads, isRunning);
    }

    private static bool IsRead(string callName) => ReadTools.Contains(ToolRef.Parse(callName).Name);

    private static List<ActiveToolInvocation> ActiveReads(ChatRunSnapshot run)
    {
        if (run.ActiveTools is null || run.ActiveTools.Count == 0) return [];
        var result = new List<ActiveToolInvocation>();
        foreach (var active in run.ActiveTools)
            if (IsRead(active.Name))
                result.Add(active);
        return result;
    }

    private static IEnumerable<KnowledgeSource> ExtractSources(string callName, string arguments)
    {
        var tool = ToolRef.Parse(callName).Name;
        var parsed = TryParse(arguments);
        switch (tool)
        {
            case "read_multiple_files":
            {
                if (parsed is { ValueKind: JsonValueKind.Object } element
                    && element.TryGetProperty("paths", out var paths)
                    && paths.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in paths.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && item.GetString() is { } path && path.Length > 0)
                            yield return new KnowledgeSource(NormalizePath(path), 0);
                    }
                }
                yield break;
            }
            case "fetch":
            {
                if (TryGetString(parsed, "url") is { } url && url.Length > 0)
                    yield return new KnowledgeSource(url, 0);
                yield break;
            }
            default:
            {
                if (TryGetString(parsed, "path") is { } path && path.Length > 0)
                    yield return new KnowledgeSource(NormalizePath(path), 0);
                yield break;
            }
        }
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static void Add(
        Dictionary<string, GroupAccumulator> groups,
        string callName,
        KnowledgeSource source)
    {
        var key = GroupKey(callName);
        if (!groups.TryGetValue(key, out var group))
        {
            var tool = ToolRef.Parse(callName);
            var server = ServerFor(tool);
            group = new GroupAccumulator(server, tool.Name, ToolLabel(tool.Name));
            groups[key] = group;
        }
        group.Add(source);
    }

    private static string GroupKey(string callName)
    {
        var tool = ToolRef.Parse(callName);
        return tool.ServerPrefix + tool.Name;
    }

    private static string ServerFor(ToolRef tool)
    {
        if (tool.IsBuiltIn) return "Built-in";
        if (tool.IsApp) return "App";
        // A bare name like "read_text_file" with no server prefix is still one of the read tools
        // registered in FileToolPresentationAdapter and WebToolPresentationAdapter — those adapters
        // own the names and never get a prefix attached. Anything else without a prefix is "Other".
        if (tool.ServerPrefix.Length == 0)
            return ReadTools.Contains(tool.Name) ? "Built-in" : "Other";
        return tool.ServerPrefix.TrimEnd('_');
    }

    private static string ToolLabel(string name) => name switch
    {
        "read_text_file" => "Read file",
        "read_multiple_files" => "Read files",
        "list_directory" => "List directory",
        "directory_tree" => "Read directory tree",
        "search_files" => "Search files",
        "grep_files" => "Search in files",
        "get_file_info" => "Inspect file",
        "list_allowed_directories" => "List allowed directories",
        "zip_list" => "List archive",
        "zip_read" => "Read archive entry",
        "fetch" => "Fetch page",
        _ => new ToolRef("__" + name, "__", name).FallbackLabel
    };

    private static JsonElement? TryParse(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return null;
        try
        {
            using var document = JsonDocument.Parse(arguments);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.Clone()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryGetString(JsonElement? element, string property)
    {
        if (element is not { ValueKind: JsonValueKind.Object } input) return null;
        if (!input.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static List<List<ChatMessageView>> SplitTurns(IReadOnlyList<ChatMessageView> branch)
    {
        var turns = new List<List<ChatMessageView>>();
        foreach (var message in branch)
        {
            if (message.Role == "User" || turns.Count == 0) turns.Add([]);
            turns[^1].Add(message);
        }
        return turns;
    }

    private sealed class GroupAccumulator(string server, string toolName, string toolLabel)
    {
        private readonly Dictionary<string, int> _sources = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _ordered = [];

        public int Calls { get; private set; }
        public string Server { get; } = server;
        public string ToolName { get; } = toolName;
        public string ToolLabel { get; } = toolLabel;

        public void Add(KnowledgeSource source)
        {
            Calls++;
            if (_sources.TryGetValue(source.Display, out var existing))
            {
                _sources[source.Display] = existing + 1;
            }
            else
            {
                _sources[source.Display] = 1;
                _ordered.Add(source.Display);
            }
        }

        public ChatKnowledgeSourceGroup ToGroup()
        {
            if (_ordered.Count == 0)
                return new ChatKnowledgeSourceGroup(Server, ToolName, ToolLabel, Calls, []);
            var recent = _ordered.TakeLast(3).Reverse()
                .Select(item => new KnowledgeSource(item, _sources[item]))
                .ToArray();
            return new ChatKnowledgeSourceGroup(Server, ToolName, ToolLabel, Calls, recent);
        }
    }
}
