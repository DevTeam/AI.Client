namespace AI.Contracts.Tools;

using System.Text.Json;

public sealed class AppSkillPresentationAdapter : BuiltInToolPresentationAdapter
{
    protected override IReadOnlySet<string> Names { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "skill_search", "run_skill" };

    protected override string Prefix => ToolRef.AppPrefix;

    public override ToolCallPresentation DescribeCall(ToolRef tool, JsonElement? arguments) =>
        tool.Name == "skill_search"
            ? new("Find skills", Argument(arguments, "query"), ToolSafety.ReadOnly)
            : new("Run skill", Argument(arguments, "skillId"), ToolSafety.Mutating);

    protected override ToolResultPresentation Describe(
        ToolRef tool, JsonElement? arguments, JsonElement? structured, ToolCallResult result)
    {
        if (tool.Name == "skill_search")
        {
            var count = structured is { } value && value.TryGetProperty("skills", out var skills)
                && skills.ValueKind == JsonValueKind.Array ? skills.GetArrayLength() : 0;
            return new ToolResultPresentation($"Found {count} skill{(count == 1 ? "" : "s")}",
                result.IsError ? ToolResultSeverity.Error : ToolResultSeverity.Ok, [], null);
        }

        var status = Text(structured, "status") ?? "Failed";
        var message = Text(structured, "message") ?? "Skill finished without a result.";
        var facts = new List<ToolFact> { new("Status", status) };
        if (Text(structured, "chatId") is { } chatId) facts.Add(new ToolFact("Chat", chatId));
        var severity = result.IsError || status is "Failed" or "Cancelled"
            ? ToolResultSeverity.Error
            : status == "Skipped" ? ToolResultSeverity.Warning : ToolResultSeverity.Ok;
        var output = structured is { } data && data.TryGetProperty("output", out var outputValue)
            && outputValue.ValueKind is not JsonValueKind.Null ? outputValue.GetRawText() : null;
        return new ToolResultPresentation(message, severity, facts, output);
    }
}
