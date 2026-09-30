namespace AI.Contracts.Tools;

using System.Text.Json;

/// <summary>Describes a request to show the user a project, chat or branch.</summary>
public sealed class AppNavigatePresentationAdapter : BuiltInToolPresentationAdapter
{
    protected override IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal) { "app_navigate" };

    protected override string Prefix => ToolRef.AppPrefix;

    public override ToolCallPresentation DescribeCall(ToolRef tool, JsonElement? arguments)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var label = Argument(arguments, "branchId") is { Length: > 0 } ? "Open branch"
            : Argument(arguments, "chatId") is { Length: > 0 } ? "Open chat"
            : "Open project";
        return new ToolCallPresentation(label, null, ToolSafety.ReadOnly);
    }

    protected override ToolResultPresentation Describe(
        ToolRef tool, JsonElement? arguments, JsonElement? structured, ToolCallResult result)
    {
        var opened = Flag(structured, "opened");
        var error = Text(structured, "error");
        return new ToolResultPresentation(
            opened ? Text(structured, "effect") ?? "Opened" : error ?? FirstText(result) ?? "Nothing was opened",
            SeverityFor(!opened),
            [],
            null);
    }
}
