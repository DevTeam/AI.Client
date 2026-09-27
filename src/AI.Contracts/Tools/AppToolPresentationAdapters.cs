namespace AI.Contracts.Tools;

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

    private static string Short(string id) => id.Length > 8 ? id[..8] : id;

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