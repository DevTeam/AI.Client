namespace AI.Application.Skills;

using System.Text.Json;
using AI.Contracts.Skills;
using Chat;
using Settings;
using Tools;

public sealed class SkillRouting(ISkillRunner runner, ISkillGuide guide, IGlobalSettingsRepository settings) : ISkillRouting
{
    /// <summary>How much of the previous answer the router sees: its end, where the question or proposal is.</summary>
    private const int PreviousLength = 1_500;

    private const int MessageLength = 4_000;

    /// <summary>A tool is offered by its first sentence; the router needs what it is for, not how to call it.</summary>
    private const int ToolDescriptionLength = 120;

    private const int MaxTools = 160;

    /// <summary>The line <c>ResourceModelProjection</c> puts before a message whose skill the user picked in the <c>/</c> list.</summary>
    private const string InvokedSkillMarker = "The user invoked the skill ";

    public async Task<SkillRoute?> RouteAsync(ToolRunContext run, IReadOnlyList<ChatCompletionMessage> context,
        IReadOnlyList<AgentTool> tools, CancellationToken cancellationToken)
    {
        if (context is not [.., { Role: "user" } latest]) return null;
        var message = latest.ForModel;
        if (message.Contains(InvokedSkillMarker, StringComparison.Ordinal)) return null;
        if ((await settings.LoadAsync(cancellationToken)).ChatAutomation is { RouteSkills: false }) return null;
        var previous = context.Take(context.Count - 1).LastOrDefault(item => item is { Role: "assistant", Content.Length: > 0 });
        var active = await guide.ActivePlaybookAsync(run.ProjectId, context, cancellationToken);
        var offered = tools
            .Where(tool => !tool.ModelDefinition.Name.EndsWith("finish_run", StringComparison.Ordinal))
            .Take(MaxTools)
            .Select(tool => $"{tool.ModelDefinition.Name}: {FirstSentence(tool.ModelDefinition.Description)}")
            .ToArray();
        var parameters = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["message"] = Head(message, MessageLength),
            ["previous"] = previous is null ? null : Tail(previous.ForModel, PreviousLength),
            ["active_skill"] = active?.Id,
            ["tools"] = offered
        }.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value));

        var result = await runner.RunAsync(new SkillInvocation(SkillRouteSkill.Id, run.ProjectId, parameters,
            run.ChatId, run.BranchId), cancellationToken);
        if (result is not { Status: "Completed", Output: { ValueKind: JsonValueKind.Object } output }) return null;
        var skills = await guide.EffectiveAsync(run.ProjectId, cancellationToken);
        var chosen = Strings(output, "skills")
            .Select(id => skills.SingleOrDefault(skill => skill.Id == id))
            .OfType<SkillDefinition>()
            .ToArray();
        return new SkillRoute(chosen, Strings(output, "tools"),
            active is not null && chosen is [{ } only] && only.Id == active.Id);
    }

    private static string[] Strings(JsonElement output, string property) =>
        output.TryGetProperty(property, out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(item => item.Length > 0).ToArray()
            : [];

    private static string FirstSentence(string description)
    {
        var flat = description.ReplaceLineEndings(" ").Trim();
        var end = flat.IndexOf(". ", StringComparison.Ordinal);
        var sentence = end > 0 ? flat[..(end + 1)] : flat;
        return sentence.Length <= ToolDescriptionLength ? sentence : sentence[..ToolDescriptionLength] + "...";
    }

    private static string Head(string text, int length) => text.Length <= length ? text : text[..length] + "...";

    private static string Tail(string text, int length) => text.Length <= length ? text : "..." + text[^length..];
}
