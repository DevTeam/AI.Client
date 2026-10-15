namespace AI.Contracts.Tools;

using System.Text.Json;
using Plans;

/// <summary>
/// The plan tool's row in the activity. The plan itself has its own card; the row only says what
/// the call did to it, so a turn's activity still reads as a list of actions.
/// </summary>
public sealed class AppPlanPresentationAdapter : BuiltInToolPresentationAdapter
{
    protected override IReadOnlySet<string> Names { get; } =
        new HashSet<string>(StringComparer.Ordinal) { ChatPlanResults.ToolName };

    protected override string Prefix => ToolRef.AppPrefix;

    public override ToolCallPresentation DescribeCall(ToolRef tool, JsonElement? arguments) =>
        ArgumentArray(arguments, "steps") is { } steps
            ? new("Lay out plan", Plural(steps.GetArrayLength(), "step", "steps"), ToolSafety.ReadOnly)
            : new("Update plan", Argument(arguments, "title"), ToolSafety.ReadOnly);

    protected override ToolResultPresentation Describe(
        ToolRef tool, JsonElement? arguments, JsonElement? structured, ToolCallResult result)
    {
        var percent = Number(structured, "percent") ?? 0;
        var total = Number(structured, "total") ?? 0;
        var completed = Number(structured, "completed") ?? 0;
        return new ToolResultPresentation($"{percent}% · {completed} of {Plural(total, "step", "steps")} done",
            ToolResultSeverity.Ok, [], null);
    }
}
