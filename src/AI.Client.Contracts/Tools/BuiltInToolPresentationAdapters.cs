namespace AI.Client.Contracts.Tools;

using System.Globalization;
using System.Text.Json;

/// <summary>
/// Shared plumbing for the adapters that know a specific built-in tool's output schema. Everything
/// here reads the tool's actual result; nothing is inferred from the tool's name alone beyond the
/// label, and a field that is absent simply produces no fact.
/// </summary>
public abstract class BuiltInToolPresentationAdapter : IToolPresentationAdapter
{
    /// <summary>Tool names this adapter speaks for, as the built-in server declares them.</summary>
    protected abstract IReadOnlySet<string> Names { get; }

    public bool CanHandle(ToolRef tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return tool.IsBuiltIn && Names.Contains(tool.Name);
    }

    public abstract ToolCallPresentation DescribeCall(ToolRef tool, JsonElement? arguments);

    public ToolResultPresentation DescribeResult(ToolRef tool, JsonElement? arguments, ToolCallResult result)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(result);
        var structured = result.StructuredContent is { ValueKind: JsonValueKind.Object } value ? value : (JsonElement?)null;

        // Built-in tools report failure in the result's own `error` field as well as `isError`,
        // and that message is the most useful thing to put on a collapsed row.
        if (Text(structured, "error") is { Length: > 0 } failure)
            return new ToolResultPresentation(failure, ToolResultSeverity.Error, [], failure);
        if (result.IsError)
            return new ToolResultPresentation("Failed", ToolResultSeverity.Error, [], FirstText(result));

        return Describe(tool, arguments, structured, result);
    }

    protected abstract ToolResultPresentation Describe(
        ToolRef tool, JsonElement? arguments, JsonElement? structured, ToolCallResult result);

    /// <summary>
    /// A completed call the user should still look at — output was cut short, a process exited
    /// non-zero. Never an error: the call did what it was asked, the outcome is just not clean.
    /// </summary>
    protected static ToolResultSeverity SeverityFor(bool warn) =>
        warn ? ToolResultSeverity.Warning : ToolResultSeverity.Ok;

    protected static string? Text(JsonElement? element, string property) =>
        element is { } value && value.TryGetProperty(property, out var found) && found.ValueKind == JsonValueKind.String
            ? found.GetString()
            : null;

    protected static int? Number(JsonElement? element, string property) =>
        element is { } value && value.TryGetProperty(property, out var found) && found.ValueKind == JsonValueKind.Number
            && found.TryGetInt64(out var number)
            ? (int)Math.Clamp(number, int.MinValue, int.MaxValue)
            : null;

    protected static long? Size(JsonElement? element, string property) =>
        element is { } value && value.TryGetProperty(property, out var found) && found.ValueKind == JsonValueKind.Number
            && found.TryGetInt64(out var number)
            ? number
            : null;

    protected static bool Flag(JsonElement? element, string property) =>
        element is { } value && value.TryGetProperty(property, out var found) && found.ValueKind == JsonValueKind.True;

    protected static int? Count(JsonElement? element, string property) =>
        element is { } value && value.TryGetProperty(property, out var found) && found.ValueKind == JsonValueKind.Array
            ? found.GetArrayLength()
            : null;

    protected static string? Argument(JsonElement? arguments, string property) =>
        arguments is { ValueKind: JsonValueKind.Object } input
        && input.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    protected static JsonElement? ArgumentArray(JsonElement? arguments, string property) =>
        arguments is { ValueKind: JsonValueKind.Object } input
        && input.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Array
            ? value
            : null;

    /// <summary>
    /// The last one or two path segments — enough to tell rows in one directory apart without
    /// spending the whole row on a path prefix every line shares. The full path stays in the
    /// expanded arguments.
    /// </summary>
    protected static string? FileLabel(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var trimmed = path.TrimEnd('/', '\\');
        var segments = trimmed.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return segments.Length switch
        {
            0 => trimmed,
            1 => segments[0],
            _ => segments[^2] + "/" + segments[^1],
        };
    }

    protected static string Plural(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    protected static string? FirstText(ToolCallResult result)
    {
        foreach (var block in result.Content)
            if (block.Kind == ToolContentKind.Text && !string.IsNullOrWhiteSpace(block.Text))
                return block.Text;
        return null;
    }
}

/// <summary>Built-in file system tools, whose structured results carry exact effects.</summary>
public sealed class FileToolPresentationAdapter : BuiltInToolPresentationAdapter
{
    protected override IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "read_text_file", "read_multiple_files", "list_directory", "directory_tree", "search_files",
        "get_file_info", "write_file", "edit_file", "create_directory", "move_file", "list_allowed_directories",
    };

    public override ToolCallPresentation DescribeCall(ToolRef tool, JsonElement? arguments)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var path = Argument(arguments, "path");
        return tool.Name switch
        {
            "read_text_file" => new ToolCallPresentation("Read file", FileLabel(path), ToolSafety.ReadOnly),
            "read_multiple_files" => new ToolCallPresentation("Read files",
                ArgumentArray(arguments, "paths") is { } paths ? Plural(paths.GetArrayLength(), "file", "files") : null,
                ToolSafety.ReadOnly),
            "list_directory" => new ToolCallPresentation("List directory", FileLabel(path), ToolSafety.ReadOnly),
            "directory_tree" => new ToolCallPresentation("Read directory tree", FileLabel(path), ToolSafety.ReadOnly),
            "search_files" => new ToolCallPresentation("Search files",
                Argument(arguments, "pattern") ?? FileLabel(path), ToolSafety.ReadOnly),
            "get_file_info" => new ToolCallPresentation("Inspect file", FileLabel(path), ToolSafety.ReadOnly),
            "list_allowed_directories" => new ToolCallPresentation("List allowed directories", null, ToolSafety.ReadOnly),
            "write_file" => new ToolCallPresentation("Write file", FileLabel(path), ToolSafety.Destructive),
            // A dry run is declared destructive by the server, but this particular invocation was
            // explicitly asked not to write, and the badge should reflect the invocation.
            "edit_file" => new ToolCallPresentation("Edit file", FileLabel(path),
                IsDryRun(arguments) ? ToolSafety.ReadOnly : ToolSafety.Destructive),
            "create_directory" => new ToolCallPresentation("Create directory", FileLabel(path), ToolSafety.Mutating),
            "move_file" => new ToolCallPresentation("Move file",
                FileLabel(Argument(arguments, "destination")), ToolSafety.Destructive),
            _ => new ToolCallPresentation(tool.FallbackLabel, FileLabel(path), ToolSafety.Unknown),
        };
    }

    protected override ToolResultPresentation Describe(
        ToolRef tool, JsonElement? arguments, JsonElement? structured, ToolCallResult result)
    {
        var truncated = Flag(structured, "truncated");
        switch (tool.Name)
        {
            case "read_text_file":
            {
                var lines = Number(structured, "lineCount");
                return new ToolResultPresentation(
                    lines is { } count ? Plural(count, "line", "lines") + (truncated ? ", truncated" : "") : "Read",
                    SeverityFor(truncated),
                    Facts(("Path", Text(structured, "path")), ("First line", Number(structured, "firstLine")?.ToString(CultureInfo.InvariantCulture)),
                        ("Truncated", truncated ? "yes" : null)),
                    Text(structured, "content"));
            }
            case "read_multiple_files":
            {
                var files = Count(structured, "files") ?? 0;
                return new ToolResultPresentation(Plural(files, "file", "files"), ToolResultSeverity.Ok, [], null);
            }
            case "list_directory":
            case "directory_tree":
            {
                var entries = Count(structured, "entries") ?? 0;
                return new ToolResultPresentation(
                    Plural(entries, "entry", "entries") + (truncated ? ", truncated" : ""),
                    SeverityFor(truncated), Facts(("Path", Text(structured, "path"))), null);
            }
            case "search_files":
            {
                var matches = Count(structured, "matches") ?? 0;
                return new ToolResultPresentation(
                    Plural(matches, "match", "matches") + (truncated ? ", truncated" : ""),
                    SeverityFor(truncated), Facts(("Path", Text(structured, "path"))), null);
            }
            case "get_file_info":
                return new ToolResultPresentation(Text(structured, "kind") ?? "Inspected", ToolResultSeverity.Ok,
                    Facts(("Kind", Text(structured, "kind")), ("Size", Bytes(Size(structured, "size"))),
                        ("Read only", Flag(structured, "isReadOnly") ? "yes" : null),
                        ("Link target", Text(structured, "linkTarget"))), null);
            case "write_file":
                return new ToolResultPresentation(
                    (Flag(structured, "created") ? "Created" : "Updated") + Suffix(Bytes(Size(structured, "bytes"))),
                    ToolResultSeverity.Ok, Facts(("Path", Text(structured, "path"))), null);
            case "edit_file":
            {
                var applied = Number(structured, "applied") ?? 0;
                var dryRun = Flag(structured, "dryRun");
                return new ToolResultPresentation(
                    Plural(applied, "edit", "edits") + (dryRun ? ", dry run" : ""),
                    ToolResultSeverity.Ok,
                    Facts(("Path", Text(structured, "path")), ("Dry run", dryRun ? "yes" : null)),
                    Text(structured, "diff"), ToolBodyFormat.Diff);
            }
            case "create_directory":
                return new ToolResultPresentation(Flag(structured, "created") ? "Created" : "Already existed",
                    ToolResultSeverity.Ok, Facts(("Path", Text(structured, "path"))), null);
            case "move_file":
                return new ToolResultPresentation("Moved", ToolResultSeverity.Ok,
                    Facts(("From", Text(structured, "source")), ("To", Text(structured, "destination"))), null);
            case "list_allowed_directories":
            {
                var directories = Count(structured, "directories") ?? 0;
                return new ToolResultPresentation(Plural(directories, "directory", "directories"),
                    ToolResultSeverity.Ok, [], null);
            }
            default:
                return new ToolResultPresentation("Done", ToolResultSeverity.Ok, [], FirstText(result));
        }
    }

    private static bool IsDryRun(JsonElement? arguments) =>
        arguments is { ValueKind: JsonValueKind.Object } input
        && input.TryGetProperty("dryRun", out var value)
        && value.ValueKind == JsonValueKind.True;

    private static string Suffix(string? value) => value is null ? "" : $" · {value}";

    private static string? Bytes(long? size) => size switch
    {
        null => null,
        < 1024 => $"{size} B",
        < 1024 * 1024 => $"{size / 1024.0:F1} KB",
        _ => $"{size / (1024.0 * 1024.0):F1} MB",
    };

    private static List<ToolFact> Facts(params (string Label, string? Value)[] facts) =>
        [.. facts.Where(fact => !string.IsNullOrWhiteSpace(fact.Value)).Select(fact => new ToolFact(fact.Label, fact.Value!))];
}

/// <summary>The built-in process runner.</summary>
public sealed class ProcessToolPresentationAdapter : BuiltInToolPresentationAdapter
{
    protected override IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal) { "process_run" };

    public override ToolCallPresentation DescribeCall(ToolRef tool, JsonElement? arguments)
    {
        var executable = Argument(arguments, "executable");
        var parts = ArgumentArray(arguments, "arguments");
        var detail = executable is null
            ? null
            : parts is { } list
                ? (executable + " " + string.Join(' ', list.EnumerateArray()
                    .Where(part => part.ValueKind == JsonValueKind.String)
                    .Select(part => part.GetString()))).Trim()
                : executable;
        return new ToolCallPresentation("Run command", Shorten(detail), ToolSafety.Destructive);
    }

    protected override ToolResultPresentation Describe(
        ToolRef tool, JsonElement? arguments, JsonElement? structured, ToolCallResult result)
    {
        var exitCode = Number(structured, "exitCode");
        var timedOut = Flag(structured, "timedOut");
        var truncated = Flag(structured, "truncated");
        var summary = timedOut ? "Timed out"
            : exitCode is { } code ? (code == 0 ? "Exit 0" : $"Exit {code}")
            : "Finished";
        var stdout = Text(structured, "stdout");
        var stderr = Text(structured, "stderr");
        var body = string.Join("\n", new[]
        {
            string.IsNullOrEmpty(stdout) ? null : stdout,
            string.IsNullOrEmpty(stderr) ? null : "stderr:\n" + stderr,
        }.Where(part => part is not null));

        return new ToolResultPresentation(
            summary + (truncated ? " · output truncated" : ""),
            // A non-zero exit is a real outcome the model will act on, not a host failure, so it
            // stays a warning: the row draws the eye without claiming the call broke.
            SeverityFor(timedOut || truncated || exitCode is not 0),
            Facts(("Exit code", exitCode?.ToString(CultureInfo.InvariantCulture)),
                ("Duration", Size(structured, "durationMs") is { } ms ? $"{ms} ms" : null),
                ("Timed out", timedOut ? "yes" : null),
                ("Output truncated", truncated ? "yes" : null)),
            string.IsNullOrEmpty(body) ? null : body);
    }

    private static string? Shorten(string? command) =>
        command is { Length: > 96 } ? command[..96] + "…" : command;

    private static List<ToolFact> Facts(params (string Label, string? Value)[] facts) =>
        [.. facts.Where(fact => !string.IsNullOrWhiteSpace(fact.Value)).Select(fact => new ToolFact(fact.Label, fact.Value!))];
}

/// <summary>The built-in web fetch tool.</summary>
public sealed class WebToolPresentationAdapter : BuiltInToolPresentationAdapter
{
    protected override IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal) { "fetch" };

    public override ToolCallPresentation DescribeCall(ToolRef tool, JsonElement? arguments) =>
        // The host is the part worth reading at a glance; the full URL stays in the arguments.
        new("Fetch page", Host(Argument(arguments, "url")), ToolSafety.ReadOnly);

    protected override ToolResultPresentation Describe(
        ToolRef tool, JsonElement? arguments, JsonElement? structured, ToolCallResult result)
    {
        var status = Number(structured, "status");
        var truncated = Flag(structured, "truncated");
        return new ToolResultPresentation(
            (status is { } code ? $"HTTP {code}" : "Fetched") + (truncated ? " · truncated" : ""),
            SeverityFor(truncated || status is not (>= 200 and < 300)),
            Facts(("Status", status?.ToString(CultureInfo.InvariantCulture)), ("Content type", Text(structured, "contentType")),
                ("URL", Text(structured, "url")), ("Truncated", truncated ? "yes" : null)),
            Text(structured, "content"));
    }

    private static string? Host(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed.Host : url;

    private static List<ToolFact> Facts(params (string Label, string? Value)[] facts) =>
        [.. facts.Where(fact => !string.IsNullOrWhiteSpace(fact.Value)).Select(fact => new ToolFact(fact.Label, fact.Value!))];
}
