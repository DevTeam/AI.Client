namespace AI.Contracts.Tools;

using System.Text.Json;

/// <summary>Describes a chat's request to read, try out, save or switch the window's colour themes.</summary>
public sealed class AppThemePresentationAdapter : BuiltInToolPresentationAdapter
{
    protected override IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal) { "app_theme" };

    protected override string Prefix => ToolRef.AppPrefix;

    public override ToolCallPresentation DescribeCall(ToolRef tool, JsonElement? arguments)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var operation = Argument(arguments, "operation")?.ToLowerInvariant();
        var (label, safety) = operation switch
        {
            "get" => ("Read themes", ToolSafety.ReadOnly),
            // A preview paints the page but writes nothing: a reload or Discard puts the saved theme back.
            "preview" => ("Preview theme", ToolSafety.ReadOnly),
            "save" => ("Save theme", ToolSafety.Mutating),
            "activate" => ("Switch theme", ToolSafety.Mutating),
            "discard" => ("Discard theme preview", ToolSafety.ReadOnly),
            _ => ("Theme", ToolSafety.Unknown)
        };
        return new ToolCallPresentation(label, Argument(arguments, "name") ?? Argument(arguments, "background") ?? Argument(arguments, "preset"), safety);
    }

    protected override ToolResultPresentation Describe(
        ToolRef tool, JsonElement? arguments, JsonElement? structured, ToolCallResult result)
    {
        var applied = Flag(structured, "applied");
        var error = Text(structured, "error");
        return new ToolResultPresentation(
            applied ? Text(structured, "effect") ?? "Done" : error ?? Text(structured, "effect") ?? FirstText(result) ?? "Nothing was changed",
            SeverityFor(!applied),
            [],
            null);
    }
}
