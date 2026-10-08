namespace AI.Application.Skills;

using System.Text.Json;
using System.Text.RegularExpressions;
using AI.Contracts.Skills;
using AI.Contracts.Tools;
using Chat;

public sealed class SkillGuide(ISkillCatalog catalog) : ISkillGuide
{
    /// <summary>
    /// How many user messages back, the current one included, a loaded playbook still counts as
    /// the task in progress. Further back the conversation has usually moved on, and a reminder
    /// about it would pull the model back into a finished task.
    /// </summary>
    public const int ActiveTurns = 4;

    private const string RunSkillTool = ToolRef.AppPrefix + "run_skill";

    public async Task<IReadOnlyList<SkillDefinition>> EffectiveAsync(Guid? projectId, CancellationToken cancellationToken) =>
        (await catalog.ListAsync(projectId == Guid.Empty ? null : projectId, cancellationToken))
            .GroupBy(skill => skill.Id, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(skill => skill.Source == "Project" ? 2 : skill.Source == "User" ? 1 : 0)
                .First())
            .Where(skill => skill.Enabled)
            .OrderBy(skill => skill.Id, StringComparer.Ordinal)
            .ToArray();

    public async Task<SkillDefinition?> ActivePlaybookAsync(Guid projectId, IReadOnlyList<ChatCompletionMessage> context,
        CancellationToken cancellationToken)
    {
        var users = 0;
        for (var index = context.Count - 1; index >= 0 && users < ActiveTurns; index--)
        {
            var message = context[index];
            if (message.Role == "user")
            {
                if (!message.JoinsTurn) users++;
                continue;
            }
            if (message is not { Role: "assistant", ToolCalls: { Count: > 0 } calls }) continue;
            for (var call = calls.Count - 1; call >= 0; call--)
            {
                if (calls[call].Name != RunSkillTool || SkillId(calls[call].Arguments) is not { } id
                    || Failed(context, index, calls[call].Id)) continue;
                var skill = (await EffectiveAsync(projectId, cancellationToken))
                    .SingleOrDefault(item => item.Id == id);
                // Only a playbook is followed across steps; any other skill finished inside its call.
                return skill?.Kind == SkillKinds.Playbook ? skill : null;
            }
        }
        return null;
    }

    private static string? SkillId(string arguments)
    {
        try
        {
            using var document = JsonDocument.Parse(arguments);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("skillId", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A call the runner rejected loaded nothing, so it names no playbook in progress. The status is
    /// read from the model's copy of the result, which is the structured run record.
    /// </summary>
    private static bool Failed(IReadOnlyList<ChatCompletionMessage> context, int from, string callId)
    {
        for (var index = from + 1; index < context.Count; index++)
            if (context[index] is { Role: "tool" } result && result.ToolCallId == callId)
                return FailedStatus.IsMatch(result.ForModel);
        return true;
    }

    private static readonly Regex FailedStatus = new("\"status\"\\s*:\\s*\"(?:Failed|Cancelled)\"",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
}
