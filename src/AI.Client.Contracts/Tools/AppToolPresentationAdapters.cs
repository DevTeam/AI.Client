namespace AI.Client.Contracts.Tools;

using System.Globalization;
using System.Text.Json;

/// <summary>
/// Describes a read of the application's own data: what was asked for, and how much of it came
/// back. Everything shown is read from the tool's declared result — nothing is inferred from the
/// arguments alone beyond naming the resource that was requested.
/// </summary>
public sealed class AppReadPresentationAdapter : BuiltInToolPresentationAdapter
{
    protected override IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal) { "app_read" };

    protected override string Prefix => ToolRef.AppPrefix;

    public override ToolCallPresentation DescribeCall(ToolRef tool, JsonElement? arguments)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var resource = Argument(arguments, "resource");
        var scope = Argument(arguments, "chatId") is { Length: > 0 } chat ? "chat " + Short(chat)
            : Argument(arguments, "projectId") is { Length: > 0 } project ? "project " + Short(project)
            : null;
        return new ToolCallPresentation(
            resource is { Length: > 0 } ? "Read " + Spaced(resource).ToLowerInvariant() : tool.FallbackLabel,
            scope,
            ToolSafety.ReadOnly);
    }

    protected override ToolResultPresentation Describe(
        ToolRef tool, JsonElement? arguments, JsonElement? structured, ToolCallResult result)
    {
        var resource = Text(structured, "resource") ?? Argument(arguments, "resource") ?? "data";
        var returned = Number(structured, "returned") ?? 0;
        var total = Number(structured, "total") ?? returned;
        var truncated = Flag(structured, "truncated");
        var more = Text(structured, "nextCursor");
        var facts = new List<ToolFact> { new("Resource", Spaced(resource)) };
        if (more is { Length: > 0 }) facts.Add(new ToolFact("Next cursor", more));
        if (truncated) facts.Add(new ToolFact("Truncated", "the character budget ended this page"));
        return new ToolResultPresentation(
            returned == total ? Plural(returned, "item", "items") : $"{returned} of {total} items",
            SeverityFor(truncated),
            facts,
            null);
    }

    internal static string Short(string id) => id.Length > 8 ? id[..8] : id;

    /// <summary>Turns an enum member's own spelling into something a person reads: <c>Messages</c>, <c>SetProjectSecurity</c>.</summary>
    internal static string Spaced(string value)
    {
        var text = new System.Text.StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            if (index > 0 && char.IsUpper(value[index]) && !char.IsUpper(value[index - 1])) text.Append(' ');
            text.Append(index == 0 ? value[index] : char.ToLowerInvariant(value[index]));
        }

        return text.ToString();
    }
}

/// <summary>
/// Describes a change to the application's own data. The tools report their effect in words as
/// part of the result, so the row says what actually happened — including when nothing did,
/// because the call was a dry run, lost a revision race, or repeated one that had already landed.
/// </summary>
public sealed class AppWritePresentationAdapter : BuiltInToolPresentationAdapter
{
    protected override IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "app_chats", "app_runs", "app_projects", "app_security",
    };

    protected override string Prefix => ToolRef.AppPrefix;

    public override ToolCallPresentation DescribeCall(ToolRef tool, JsonElement? arguments)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var operation = Argument(arguments, "operation");
        var detail = Argument(arguments, "title") ?? Argument(arguments, "name") ?? Argument(arguments, "content");
        return new ToolCallPresentation(
            operation is { Length: > 0 } ? AppReadPresentationAdapter.Spaced(operation) : tool.FallbackLabel,
            detail is { Length: > 0 } ? Trim(detail) : null,
            // The tools that can delete announce themselves as destructive; app_runs changes state
            // without ever removing history.
            tool.Name == "app_runs" ? ToolSafety.Mutating : ToolSafety.Destructive);
    }

    public override ToolResultPresentation DescribeResult(ToolRef tool, JsonElement? arguments, ToolCallResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var structured = result.StructuredContent is { ValueKind: JsonValueKind.Object } value ? value : (JsonElement?)null;
        var effect = Text(structured, "effect");
        var status = Text(structured, "status");
        var error = Text(structured, "error");
        var facts = new List<ToolFact>();
        if (status is { Length: > 0 }) facts.Add(new ToolFact("Status", status));
        if (Size(structured, "revision") is { } revision and > 0)
            facts.Add(new ToolFact("Revision", revision.ToString(CultureInfo.InvariantCulture)));
        foreach (var name in new[] { "projectId", "chatId", "branchId", "messageId" })
            if (Text(structured, name) is { Length: > 0 } id)
                facts.Add(new ToolFact(AppReadPresentationAdapter.Spaced(name), id));
        if (Flag(structured, "replayed"))
            facts.Add(new ToolFact("Replayed", "this operation id had already been applied"));

        // A conflict and a dry run both leave the data untouched, which the user should notice
        // without it reading as a failure of the tool itself.
        if (error is { Length: > 0 })
            return new ToolResultPresentation(error, status == "Conflict" ? ToolResultSeverity.Warning : ToolResultSeverity.Error,
                facts, effect);
        if (result.IsError)
            return new ToolResultPresentation("Failed", ToolResultSeverity.Error, facts, FirstText(result));
        return new ToolResultPresentation(
            effect is { Length: > 0 } ? effect : tool.FallbackLabel,
            Flag(structured, "dryRun") ? ToolResultSeverity.Warning : ToolResultSeverity.Ok,
            facts,
            null);
    }

    protected override ToolResultPresentation Describe(
        ToolRef tool, JsonElement? arguments, JsonElement? structured, ToolCallResult result) =>
        throw new NotSupportedException("This adapter describes results itself.");

    private static string Trim(string value) => value.Length <= 80 ? value : value[..79] + "…";
}

/// <summary>
/// Describes delegated work. The row says what was asked and what came back; the expandable body
/// carries the subtask's whole exchange, which the model never sees. Showing it is the point — work
/// a person cannot inspect is work they cannot trust, and it costs them nothing to have here since
/// the context saving comes from keeping it out of the model's reply, not out of the record.
/// </summary>
public sealed class AppSubtaskPresentationAdapter : BuiltInToolPresentationAdapter
{
    protected override IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal) { "spawn_subtask" };

    protected override string Prefix => ToolRef.AppPrefix;

    public override ToolCallPresentation DescribeCall(ToolRef tool, JsonElement? arguments)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var tasks = ArgumentArray(arguments, "tasks");
        var count = tasks?.GetArrayLength() ?? 0;
        // A task is an object carrying its own connection now, and was a bare string before; a
        // result written by the earlier shape still has to describe itself.
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
        var outcomes = structured is { } value && value.TryGetProperty("results", out var found)
            && found.ValueKind == JsonValueKind.Array
            ? found.EnumerateArray().ToArray()
            : [];
        var failed = outcomes.Count(item => item.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String);
        var facts = new List<ToolFact>();
        for (var index = 0; index < outcomes.Length; index++)
        {
            var outcome = outcomes[index];
            var calls = Number(outcome, "toolCalls") ?? 0;
            var files = Number(outcome, "filesChanged") ?? 0;
            var note = Text(outcome, "error") ?? Text(outcome, "answer") ?? string.Empty;
            var connection = Text(outcome, "connection");
            // The label is a label: a whole task prompt runs to several sentences, and a definition
            // list renders that as a wall. The prompts are already in the arguments below.
            facts.Add(new ToolFact($"Subtask {index + 1}",
                (connection is { Length: > 0 } ? connection + " · " : string.Empty)
                + $"{(calls == 0 ? "no tool calls" : Plural(calls, "tool call", "tool calls"))}"
                + (files > 0 ? $", {Plural(files, "file", "files")} changed" : string.Empty)
                + (note.Length > 0 ? $" — {(note.Length <= 160 ? note : note[..159] + "…")}" : string.Empty)));
        }

        return new ToolResultPresentation(
            failed == 0
                ? Plural(outcomes.Length, "subtask finished", "subtasks finished")
                : $"{failed} of {outcomes.Length} subtasks failed",
            failed == 0 ? ToolResultSeverity.Ok : ToolResultSeverity.Warning,
            facts,
            Transcript(result));
    }

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
