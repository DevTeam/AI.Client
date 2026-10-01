namespace AI.Web.Widgets;

using AI.Contracts.Chats;
using AI.Contracts.Runs;
using AI.Contracts.Tools;

public enum ChatToolOutcome { Succeeded, Failed, Running, AwaitingResult, Unknown, NoResult }

public sealed record ChatToolStatistic(string Name, string Label, string Server, int Calls, int Errors);

public sealed record ChatToolStatistics(
    IReadOnlyList<ChatToolStatistic> Tools,
    IReadOnlyDictionary<ChatToolOutcome, int> Outcomes,
    int Calls,
    int Turns,
    int TurnsWithTools,
    bool IsRunning)
{
    public int Count(ChatToolOutcome outcome) => Outcomes.GetValueOrDefault(outcome);
}

public interface IChatToolStatisticsCalculator
{
    ChatToolStatistics Calculate(IReadOnlyList<ChatMessageView> branch, ChatRunSnapshot? run, ChatWidgetScope scope);
}

/// <summary>Counts calls once by id and pairs their results within the visible turn.</summary>
public sealed class ChatToolStatisticsCalculator(IToolResultCodec codec) : IChatToolStatisticsCalculator
{
    public ChatToolStatistics Calculate(IReadOnlyList<ChatMessageView> branch, ChatRunSnapshot? run, ChatWidgetScope scope)
    {
        var turns = new List<List<ChatMessageView>>();
        foreach (var message in branch)
        {
            if (message.Role == "User" || turns.Count == 0) turns.Add([]);
            turns[^1].Add(message);
        }

        // A snapshot for a different branch, or an earlier turn, must not leak into this scope.
        var isRunning = run is { Status: ChatRunStatus.Generating or ChatRunStatus.Paused, ActiveMessageId: { } active }
            && turns.Count > 0 && turns[^1].Any(message => message.Id == active);
        if (scope == ChatWidgetScope.LastTurn && turns.Count > 1) turns = turns[^1..];

        var calls = new List<(string Name, ChatToolOutcome Outcome)>();
        var turnsWithTools = 0;
        for (var index = 0; index < turns.Count; index++)
        {
            var turn = turns[index];
            var live = isRunning && index == turns.Count - 1;
            var activeTools = live ? run!.ActiveTools ?? [] : [];
            var results = turn.Where(message => message.Role == "Tool" && message.ToolCallId is not null)
                .GroupBy(message => message.ToolCallId!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
            var invocations = turn.Where(message => message.Role == "Assistant")
                .SelectMany(message => message.ToolCalls ?? [])
                .Select(call => (call.Id, call.Name))
                .Concat(activeTools.Select(call => (Id: call.CallId, call.Name)))
                .DistinctBy(call => call.Id, StringComparer.Ordinal).ToArray();
            if (invocations.Length > 0) turnsWithTools++;
            foreach (var call in invocations)
            {
                ChatToolOutcome outcome;
                if (results.TryGetValue(call.Id, out var result))
                {
                    var decoded = result.ContentOmitted || result.IsIncomplete ? null : codec.TryRead(result.Content);
                    var isError = result.IsIncomplete ? null : result.ToolResultIsError ?? decoded?.IsError;
                    outcome = isError is null ? ChatToolOutcome.Unknown
                        : isError.Value ? ChatToolOutcome.Failed : ChatToolOutcome.Succeeded;
                }
                else if (activeTools.Any(tool => tool.CallId == call.Id)) outcome = ChatToolOutcome.Running;
                else outcome = live ? ChatToolOutcome.AwaitingResult : ChatToolOutcome.NoResult;
                calls.Add((call.Name, outcome));
            }
        }

        var tools = calls.GroupBy(call => call.Name, StringComparer.Ordinal).Select(group =>
        {
            var tool = ToolRef.Parse(group.Key);
            var server = tool.IsBuiltIn ? "Built-in" : tool.IsApp ? "App" : tool.ServerPrefix.TrimEnd('_');
            return new ChatToolStatistic(group.Key, tool.FallbackLabel, server, group.Count(),
                group.Count(call => call.Outcome == ChatToolOutcome.Failed));
        }).OrderByDescending(tool => tool.Calls).ThenBy(tool => tool.Name, StringComparer.Ordinal).ToArray();
        return new ChatToolStatistics(tools,
            calls.GroupBy(call => call.Outcome).ToDictionary(group => group.Key, group => group.Count()),
            calls.Count, turns.Count, turnsWithTools, isRunning);
    }
}
