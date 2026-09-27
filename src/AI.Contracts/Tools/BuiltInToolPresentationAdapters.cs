namespace AI.Contracts.Tools;

using System.Text.Json;

/// <summary>
/// Shared plumbing for the adapters that know a specific built-in tool's output schema. Everything
/// here reads the tool's actual result; nothing is inferred from the tool's name alone beyond the
/// label, and a field that is absent simply produces no fact.
/// </summary>
public abstract class BuiltInToolPresentationAdapter : IToolPresentationAdapter
{
    /// <summary>Tool names this adapter speaks for, as the server declares them.</summary>
    protected abstract IReadOnlySet<string> Names { get; }

    /// <summary>
    /// Which server's tools these names belong to. Names are only unique within one server, so an
    /// adapter that ignored the prefix would happily describe a third-party tool that happens to
    /// share a name with a built-in one.
    /// </summary>
    protected virtual string Prefix => ToolRef.BuiltInPrefix;

    public bool CanHandle(ToolRef tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return tool.ServerPrefix == Prefix && Names.Contains(tool.Name);
    }

    public abstract ToolCallPresentation DescribeCall(ToolRef tool, JsonElement? arguments);

    public virtual ToolResultPresentation DescribeResult(ToolRef tool, JsonElement? arguments, ToolCallResult result)
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