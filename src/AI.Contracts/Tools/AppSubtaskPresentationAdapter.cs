using System.Text.Json;

namespace AI.Contracts.Tools;

/// <summary>
/// Describes delegated work. The row says what was asked and what came back; the expandable body
/// carries the subtask's whole exchange, which the model never sees. Showing it is the point — work
/// a person cannot inspect is work they cannot trust, and it costs them nothing to have here since
/// the context saving comes from keeping it out of the model's reply, not out of the record.
/// </summary>
public sealed class AppSubtaskPresentationAdapter : BuiltInToolPresentationAdapter
{
    protected override IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal) { "spawn_subtask", "subtask_wait", "subtask_stop" };

    protected override string Prefix => ToolRef.AppPrefix;

    public override ToolCallPresentation DescribeCall(ToolRef tool, JsonElement? arguments)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (tool.Name is "subtask_wait" or "subtask_stop")
            return new ToolCallPresentation(tool.Name == "subtask_wait" ? "Collect subtasks" : "Stop subtasks",
                $"{ArgumentArray(arguments, "taskIds")?.GetArrayLength() ?? 0} selected",
                tool.Name == "subtask_wait" ? ToolSafety.ReadOnly : ToolSafety.Mutating);
        if (Argument(arguments, "task") is { Length: > 0 } task)
            return new ToolCallPresentation("Delegate a subtask", task.Length <= 80 ? task : task[..79] + "…", ToolSafety.Mutating);
        var tasks = ArgumentArray(arguments, "tasks");
        var count = tasks?.GetArrayLength() ?? 0;
        // Older history stored a batch of objects or bare task strings. Keep those records readable.
        var first = count == 0 ? null
            : tasks!.Value[0] is { ValueKind: JsonValueKind.Object } item ? Text(item, "task")
            : tasks.Value[0].GetString();
        return new ToolCallPresentation(
            count > 1 ? $"Delegate {count} subtasks" : "Delegate a subtask",
            first is { Length: > 0 } ? (first.Length <= 80 ? first : first[..79] + "…") : null,
            // A subtask can do anything its parent can, so it claims no less than that.
            ToolSafety.Mutating);
    }

    protected override ToolResultPresentation Describe(
        ToolRef tool, JsonElement? arguments, JsonElement? structured, ToolCallResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var handles = structured is { ValueKind: JsonValueKind.Object } root
            ? root.TryGetProperty("tasks", out var selected) && selected.ValueKind == JsonValueKind.Array
                ? selected.EnumerateArray().ToArray()
                : root.TryGetProperty("taskId", out _) ? [root] : []
            : [];
        var outcomes = handles.Length > 0
            ? handles.Where(item => item.TryGetProperty("result", out var outcome) && outcome.ValueKind == JsonValueKind.Object)
                .Select(item => item.GetProperty("result")).ToArray()
            : structured is { } value && value.TryGetProperty("results", out var found)
                                               && found.ValueKind == JsonValueKind.Array
            ? found.EnumerateArray().ToArray()
            : [];
        var failed = outcomes.Count(item => item.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String);
        var evidenceIssues = outcomes.Sum(item =>
            ArrayLength(item, "unresolved") + Array(item, "checks").Count(check =>
                Text(check, "status") is "Failed" or "NotRun"));
        var facts = new List<ToolFact>();
        foreach (var handle in handles)
            facts.Add(new ToolFact(Text(handle, "taskId") is { Length: >= 8 } id ? id[..8] : "Subtask",
                Text(handle, "activity") ?? Text(handle, "status") ?? "unknown"));
        for (var index = 0; index < outcomes.Length; index++)
        {
            var outcome = outcomes[index];
            var calls = Number(outcome, "toolCalls") ?? 0;
            var files = Number(outcome, "filesChanged") ?? 0;
            var elapsed = Number(outcome, "elapsedMilliseconds");
            var checks = Array(outcome, "checks");
            var passed = checks.Count(check => Text(check, "status") == "Passed");
            var unresolved = ArrayLength(outcome, "unresolved");
            var note = Text(outcome, "error") ?? Text(outcome, "answer") ?? string.Empty;
            var connection = Text(outcome, "connection");
            // The label is a label: a whole task prompt runs to several sentences, and a definition
            // list renders that as a wall. The prompts are already in the arguments below.
            facts.Add(new ToolFact($"Subtask {index + 1}",
                (connection is { Length: > 0 } ? connection + " · " : string.Empty)
                + $"{(calls == 0 ? "no tool calls" : Plural(calls, "tool call", "tool calls"))}"
                + (elapsed is { } milliseconds ? $", {milliseconds} ms" : string.Empty)
                + (files > 0 ? $", {Plural(files, "file", "files")} changed" : string.Empty)
                + (passed > 0 ? $", {Plural(passed, "check", "checks")} passed" : string.Empty)
                + (unresolved > 0 ? $", {Plural(unresolved, "unresolved item", "unresolved items")}" : string.Empty)
                + (note.Length > 0 ? $" — {(note.Length <= 160 ? note : note[..159] + "…")}" : string.Empty)));
        }

        var pending = handles.Count(item => Text(item, "status") is "queued" or "running");
        var cancelled = handles.Count(item => Text(item, "status") == "cancelled");
        return new ToolResultPresentation(
            pending > 0 ? Plural(pending, "subtask running", "subtasks running")
            : cancelled > 0 ? Plural(cancelled, "subtask cancelled", "subtasks cancelled")
            : failed == 0
                ? evidenceIssues == 0
                    ? Plural(outcomes.Length, "subtask finished", "subtasks finished")
                    : $"{evidenceIssues} evidence {(evidenceIssues == 1 ? "issue" : "issues")} need attention"
                : $"{failed} of {outcomes.Length} subtasks failed",
            failed == 0 && evidenceIssues == 0 && cancelled == 0 ? ToolResultSeverity.Ok : ToolResultSeverity.Warning,
            facts,
            Transcript(result));
    }

    private static JsonElement[] Array(JsonElement value, string name) =>
        value.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().ToArray()
            : [];

    private static int ArrayLength(JsonElement value, string name) => Array(value, name).Length;

    /// <summary>
    /// Renders the exchange the Host kept in <c>_meta</c>. It is absent from a result stored before
    /// this existed, and from one an older Host wrote, so its absence is normal rather than an error.
    /// </summary>
    private static string? Transcript(ToolCallResult result)
    {
        if (result.Meta is not { ValueKind: JsonValueKind.Object } meta) return null;
        if (!meta.TryGetProperty("transcript", out var entries) || entries.ValueKind != JsonValueKind.Array) return null;
        var text = new System.Text.StringBuilder();
        foreach (var entry in entries.EnumerateArray())
        {
            var role = Text(entry, "role") ?? "?";
            var name = Text(entry, "toolName");
            var content = Text(entry, "content") ?? string.Empty;
            if (text.Length > 0) text.AppendLine();
            text.Append(role).Append(name is { Length: > 0 } ? $" → {name}" : string.Empty).Append(": ").AppendLine(content);
        }

        return text.Length == 0 ? null : text.ToString();
    }
}
