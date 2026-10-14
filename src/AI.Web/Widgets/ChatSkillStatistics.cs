namespace AI.Web.Widgets;

using System.Text.Json;
using AI.Contracts.Chats;
using AI.Contracts.Resources;
using AI.Contracts.Runs;
using AI.Contracts.Skills;
using AI.Contracts.Tools;

/// <summary>
/// How an assistant's skill run ended. A skill invoked from the slash list has no outcome of its
/// own: its instructions travel with the message and the turn that answers it is the result.
/// </summary>
public enum ChatSkillOutcome { Succeeded, Skipped, Failed, Running, AwaitingResult, Unknown, NoResult }

/// <param name="Name">The catalog name, else the name stored with the message, else the id.</param>
/// <param name="Kind">A <see cref="SkillKinds"/> value when the catalog knows the skill, otherwise null.</param>
/// <param name="Requested">Uses from the slash list or a schedule: a message that carried the skill.</param>
/// <param name="ByAssistant">Uses the assistant started with <c>run_skill</c>.</param>
/// <param name="Messages">The messages in scope that used the skill, oldest first, each once.</param>
public sealed record ChatSkillStatistic(string Id, string Name, string? Icon, string? Kind,
    int Uses, int Requested, int ByAssistant, int Failed, bool IsRunning, IReadOnlyList<Guid> Messages);

public sealed record ChatSkillStatistics(
    IReadOnlyList<ChatSkillStatistic> Skills,
    IReadOnlyDictionary<ChatSkillOutcome, int> Outcomes,
    int Uses,
    int Requested,
    int ByAssistant,
    int Searches,
    int Turns,
    int TurnsWithSkills,
    bool IsRunning)
{
    public static ChatSkillStatistics Empty { get; } =
        new([], new Dictionary<ChatSkillOutcome, int>(), 0, 0, 0, 0, 0, 0, false);

    public int Count(ChatSkillOutcome outcome) => Outcomes.GetValueOrDefault(outcome);
}

public interface IChatSkillStatisticsCalculator
{
    /// <param name="skills">The project's effective skills, for names, icons and kinds; may be empty.</param>
    ChatSkillStatistics Calculate(IReadOnlyList<ChatMessageView> branch, ChatRunSnapshot? run,
        IReadOnlyList<SkillDefinition> skills, ChatWidgetScope scope);
}

/// <summary>
/// Adds up the skills the visible branch used: the skill a message carried from the slash list,
/// and every <c>run_skill</c> call of the assistant, counted once by call id and paired with its
/// result within the turn. Skill searches are counted beside them, since they find skills rather
/// than run one.
/// </summary>
public sealed class ChatSkillStatisticsCalculator(IToolResultCodec codec) : IChatSkillStatisticsCalculator
{
    private const string RunSkill = "run_skill";
    private const string SkillSearch = "skill_search";

    public ChatSkillStatistics Calculate(IReadOnlyList<ChatMessageView> branch, ChatRunSnapshot? run,
        IReadOnlyList<SkillDefinition> skills, ChatWidgetScope scope)
    {
        var turns = new List<List<ChatMessageView>>();
        foreach (var message in branch)
        {
            if (message.StartsTurn || turns.Count == 0) turns.Add([]);
            turns[^1].Add(message);
        }

        // A snapshot for a different branch, or an earlier turn, must not leak into this scope.
        var isRunning = run is { Status: ChatRunStatus.Generating or ChatRunStatus.Paused, ActiveMessageId: { } active }
            && turns.Count > 0 && turns[^1].Any(message => message.Id == active);
        if (scope == ChatWidgetScope.LastTurn && turns.Count > 1) turns = turns[^1..];

        var uses = new List<Use>();
        var searches = 0;
        var turnsWithSkills = 0;
        for (var index = 0; index < turns.Count; index++)
        {
            var turn = turns[index];
            var live = isRunning && index == turns.Count - 1;
            var activeTools = live ? run!.ActiveTools ?? [] : [];
            var before = uses.Count;

            foreach (var message in turn.Where(message => message.Role == "User"))
            foreach (var skill in (message.Resources ?? []).Where(item => item.Kind == ChatResourceKind.Skill)
                         .DistinctBy(item => item.Path, StringComparer.Ordinal))
                uses.Add(new Use(skill.Path, skill.Name, true, null, message.Id));

            var results = turn.Where(message => message.Role == "Tool" && message.ToolCallId is not null)
                .GroupBy(message => message.ToolCallId!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
            var invocations = turn.Where(message => message.Role == "Assistant")
                .SelectMany(message => (message.ToolCalls ?? []).Select(call => (call.Id, call.Name, call.Arguments, MessageId: (Guid?)message.Id)))
                .Concat(activeTools.Select(call => (Id: call.CallId, call.Name, call.Arguments, MessageId: run!.ActiveMessageId)))
                .DistinctBy(call => call.Id, StringComparer.Ordinal);
            foreach (var call in invocations)
            {
                var tool = ToolRef.Parse(call.Name);
                if (!tool.IsApp) continue;
                if (tool.Name == SkillSearch) { searches++; continue; }
                if (tool.Name != RunSkill || SkillId(call.Arguments) is not { } skillId) continue;

                ChatSkillOutcome outcome;
                if (results.TryGetValue(call.Id, out var result)) outcome = Outcome(result);
                else if (activeTools.Any(item => item.CallId == call.Id)) outcome = ChatSkillOutcome.Running;
                else outcome = live ? ChatSkillOutcome.AwaitingResult : ChatSkillOutcome.NoResult;
                uses.Add(new Use(skillId, null, false, outcome, call.MessageId));
            }
            if (uses.Count > before) turnsWithSkills++;
        }

        var catalog = skills.GroupBy(skill => skill.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var statistics = uses.GroupBy(use => use.SkillId, StringComparer.Ordinal).Select(group =>
        {
            var definition = catalog.GetValueOrDefault(group.Key);
            var name = definition?.Name ?? group.Select(use => use.Name).LastOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? group.Key;
            return new ChatSkillStatistic(group.Key, name, definition?.Icon, definition?.Kind, group.Count(),
                group.Count(use => use.Requested), group.Count(use => !use.Requested),
                group.Count(use => use.Outcome == ChatSkillOutcome.Failed),
                group.Any(use => use.Outcome is ChatSkillOutcome.Running or ChatSkillOutcome.AwaitingResult),
                group.Where(use => use.MessageId is not null).Select(use => use.MessageId!.Value).Distinct().ToArray());
        }).OrderByDescending(skill => skill.Uses).ThenBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase).ToArray();

        return new ChatSkillStatistics(statistics,
            uses.Where(use => use.Outcome is not null).GroupBy(use => use.Outcome!.Value)
                .ToDictionary(group => group.Key, group => group.Count()),
            uses.Count, uses.Count(use => use.Requested), uses.Count(use => !use.Requested),
            searches, turns.Count, turnsWithSkills, isRunning);
    }

    /// <summary>
    /// The transcript keeps a tool result's error flag even when it leaves the body out, so an
    /// error is always known; Skipped needs the body, and without it a clean result is Succeeded.
    /// </summary>
    private ChatSkillOutcome Outcome(ChatMessageView result)
    {
        if (result.IsIncomplete) return ChatSkillOutcome.Unknown;
        var decoded = result.ContentOmitted ? null : codec.TryRead(result.Content);
        var status = decoded?.StructuredContent is { ValueKind: JsonValueKind.Object } structured
            && structured.TryGetProperty("status", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
        if (status is "Failed" or "Cancelled") return ChatSkillOutcome.Failed;
        if (status == "Skipped") return ChatSkillOutcome.Skipped;
        return (result.ToolResultIsError ?? decoded?.IsError) switch
        {
            true => ChatSkillOutcome.Failed,
            false => ChatSkillOutcome.Succeeded,
            null => ChatSkillOutcome.Unknown
        };
    }

    private static string? SkillId(string arguments)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("skillId", out var id) && id.ValueKind == JsonValueKind.String
                && id.GetString() is { Length: > 0 } value ? value : null;
        }
        catch (JsonException)
        {
            // Arguments still streaming, or malformed: the call names no skill yet.
            return null;
        }
    }

    private sealed record Use(string SkillId, string? Name, bool Requested, ChatSkillOutcome? Outcome, Guid? MessageId);
}
