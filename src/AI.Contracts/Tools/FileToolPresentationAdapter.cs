using System.Globalization;
using System.Text.Json;

namespace AI.Contracts.Tools;

/// <summary>Built-in file system tools, whose structured results carry exact effects.</summary>
public sealed class FileToolPresentationAdapter : BuiltInToolPresentationAdapter
{
    protected override IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "read_text_file", "read_multiple_files", "list_directory", "directory_tree", "search_files", "grep_files",
        "get_file_info", "write_file", "edit_file", "create_directory", "move_file", "delete_file", "delete_directory",
        "list_allowed_directories",
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
            "grep_files" => new ToolCallPresentation("Search in files",
                Argument(arguments, "query") ?? FileLabel(path), ToolSafety.ReadOnly),
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
            // A recursive delete removes a whole subtree, which is worth saying on the row itself
            // rather than leaving to the expanded arguments.
            "delete_file" => new ToolCallPresentation("Delete file", FileLabel(path), ToolSafety.Destructive),
            "delete_directory" => new ToolCallPresentation(
                FlagArgument(arguments, "recursive") ? "Delete directory (recursive)" : "Delete directory",
                FileLabel(path), ToolSafety.Destructive),
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
            case "grep_files":
            {
                // The headline counts matching lines, not files, because that is what the caller
                // asked for; the file count and what was skipped stay available as facts.
                var total = Number(structured, "totalMatches") ?? 0;
                var files = Count(structured, "files") ?? 0;
                var skipped = Number(structured, "filesSkipped") ?? 0;
                return new ToolResultPresentation(
                    $"{Plural(total, "match", "matches")} in {Plural(files, "file", "files")}" + (truncated ? ", truncated" : ""),
                    SeverityFor(truncated),
                    Facts(("Path", Text(structured, "path")), ("Query", Text(structured, "query")),
                        ("Files scanned", Number(structured, "filesScanned")?.ToString(CultureInfo.InvariantCulture)),
                        ("Files skipped", skipped > 0 ? skipped.ToString(CultureInfo.InvariantCulture) : null),
                        ("Truncated", truncated ? "yes" : null)),
                    null);
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
            case "delete_file":
                return new ToolResultPresentation("Deleted" + Suffix(Bytes(Size(structured, "bytes"))),
                    ToolResultSeverity.Ok, Facts(("Path", Text(structured, "path"))), null);
            case "delete_directory":
                return new ToolResultPresentation(
                    Flag(structured, "recursive") ? "Deleted, recursive" : "Deleted",
                    ToolResultSeverity.Ok,
                    Facts(("Path", Text(structured, "path")), ("Recursive", Flag(structured, "recursive") ? "yes" : null)),
                    null);
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

    private static bool FlagArgument(JsonElement? arguments, string property) =>
        arguments is { ValueKind: JsonValueKind.Object } input
        && input.TryGetProperty(property, out var value)
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