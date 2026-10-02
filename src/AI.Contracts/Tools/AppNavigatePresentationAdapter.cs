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
        var action = Argument(arguments, "action");
        var target = Argument(arguments, "target");
        var label = action == "targets" ? "Discover application controls"
            : target is { Length: > 0 } ? action switch
            {
                "show" => "Show application control", "hover" => "Point to application control",
                "focus" => "Focus editor", "set_value" => "Edit application control", _ => "Open application control"
            }
            : Argument(arguments, "branchId") is { Length: > 0 } ? "Open branch"
            : Argument(arguments, "chatId") is { Length: > 0 } ? "Open chat"
            : "Open project";
        return new ToolCallPresentation(label, target, action == "set_value" ? ToolSafety.Mutating : ToolSafety.ReadOnly);
    }

    protected override ToolResultPresentation Describe(
        ToolRef tool, JsonElement? arguments, JsonElement? structured, ToolCallResult result)
    {
        var opened = Flag(structured, "opened");
        var error = Text(structured, "error");
        var success = opened || Text(structured, "outcome") == "targets";
        return new ToolResultPresentation(
            success ? Text(structured, "effect") ?? "Shown" : error ?? Text(structured, "effect") ?? FirstText(result) ?? "Nothing was opened",
            SeverityFor(!success),
            [],
            null);
    }
}
