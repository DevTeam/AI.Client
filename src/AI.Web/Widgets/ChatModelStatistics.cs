namespace AI.Web.Widgets;

using AI.Contracts.Chats;
using AI.Contracts.Usage;

/// <summary>
/// One model that answered in the scope, with how many answer requests it produced. Models are
/// counted from <see cref="TurnTokenUsage.AnswerModels"/>, which the Host records per answer request
/// including interrupted streams; a model that only drafted a chat title or judged a tool call is not
/// an answering model and does not appear here.
/// </summary>
/// <param name="Model">The model name as the endpoint reported it; two spellings stay two rows.</param>
/// <param name="Requests">Answer requests this model served in the scope.</param>
/// <param name="Share">Part of the scope's answer requests, from 0 to 1; 0 when there were none.</param>
/// <param name="FirstAt">When its earliest recorded request in the scope was made.</param>
/// <param name="LastAt">When its latest recorded request in the scope was made.</param>
public sealed record ChatModelEntry(
    string Model,
    int Requests,
    double Share,
    DateTimeOffset FirstAt,
    DateTimeOffset LastAt);

/// <summary>
/// Which models answered the chat and how much of the answering each one did, in the chosen scope.
/// Only request counts are available: the chat ledger groups tokens by purpose, not by model, so the
/// widget says nothing about how many tokens belonged to which model.
/// </summary>
/// <param name="Models">Answering models, most requests first.</param>
/// <param name="Requests">Answer requests in the scope, summed over models.</param>
/// <param name="Turns">How many turns the scope covers; matches the turn count the other widgets report.</param>
/// <param name="TurnsWithAnswers">Turns in which at least one answer request was recorded.</param>
/// <param name="IsRunning">True while the scope's last turn is still going; the widget says so far.</param>
public sealed record ChatModelStatistics(
    IReadOnlyList<ChatModelEntry> Models,
    int Requests,
    int Turns,
    int TurnsWithAnswers,
    bool IsRunning)
{
    public static ChatModelStatistics Empty { get; } = new([], 0, 0, 0, false);

    public bool HasModels => Models.Count > 0;
}

/// <summary>Builds the answering-model figures for the visible branch and the chosen scope.</summary>
public interface IChatModelStatisticsCalculator
{
    /// <param name="branch">The visible branch, root to leaf.</param>
    /// <param name="isRunning">True while the last turn is still going; the widget says so far.</param>
    /// <param name="scope">Whole chat or last turn.</param>
    ChatModelStatistics Calculate(
        ChatTokenUsage? chatUsage,
        TurnTokenUsage? liveTurn,
        IReadOnlyList<ChatMessageView> branch,
        bool isRunning,
        ChatWidgetScope scope);
}

public sealed class ChatModelStatisticsCalculator : IChatModelStatisticsCalculator
{
    public ChatModelStatistics Calculate(
        ChatTokenUsage? chatUsage,
        TurnTokenUsage? liveTurn,
        IReadOnlyList<ChatMessageView> branch,
        bool isRunning,
        ChatWidgetScope scope)
    {
        var turns = SplitTurns(branch);
        if (scope == ChatWidgetScope.LastTurn && turns.Count > 1) turns = turns[^1..];

        // Whole chat or last turn. The turn is chosen with the same rule Performance and Subtasks use,
        // so the widgets agree on which turn "the last turn" is.
        var answers = scope == ChatWidgetScope.LastTurn
            ? ChooseTurn(chatUsage, liveTurn)?.AnswerModels ?? []
            : AllAnswers(chatUsage);
        var turnsWithAnswers = scope == ChatWidgetScope.LastTurn
            ? answers.Count > 0 ? 1 : 0
            : (chatUsage?.Turns ?? []).Count(turn => turn.AnswerModels is { Count: > 0 });

        var grouped = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        foreach (var answer in answers)
        {
            if (string.IsNullOrWhiteSpace(answer.Model)) continue;
            if (grouped.TryGetValue(answer.Model, out var known)) known.Add(answer.At);
            else grouped[answer.Model] = new Accumulator(answer.At);
        }

        var total = grouped.Values.Sum(entry => entry.Requests);
        var models = grouped
            .OrderByDescending(pair => pair.Value.Requests)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ChatModelEntry(pair.Key, pair.Value.Requests,
                total > 0 ? (double)pair.Value.Requests / total : 0, pair.Value.FirstAt, pair.Value.LastAt))
            .ToArray();

        return new ChatModelStatistics(models, total, turns.Count, turnsWithAnswers,
            isRunning && scope == ChatWidgetScope.LastTurn);
    }

    private static List<AnswerModelUsage> AllAnswers(ChatTokenUsage? chatUsage)
    {
        if (chatUsage is null) return [];
        var answers = new List<AnswerModelUsage>();
        foreach (var turn in chatUsage.Turns)
        {
            if (turn.AnswerModels is { Count: > 0 } recorded) answers.AddRange(recorded);
        }
        return answers;
    }

    // The same chat-vs-turn rule ChatPerformanceCalculator and ChatSubtaskStatisticsCalculator use: a
    // live turn replaces the stored one when its request count is at least as high, or when it started
    // no earlier than the stored one.
    private static TurnTokenUsage? ChooseTurn(ChatTokenUsage? chatUsage, TurnTokenUsage? liveTurn)
    {
        var stored = chatUsage is { Turns.Count: > 0 } ? chatUsage.Turns[^1] : null;
        if (liveTurn is null) return stored;
        if (stored is null) return liveTurn;
        if (stored.TurnId == liveTurn.TurnId && liveTurn.Totals.Requests >= stored.Totals.Requests) return liveTurn;
        if (stored.TurnId != liveTurn.TurnId && liveTurn.StartedAt >= stored.StartedAt) return liveTurn;
        return stored;
    }

    // A turn starts at a person's message; whatever comes before the first one belongs to it.
    private static List<List<ChatMessageView>> SplitTurns(IReadOnlyList<ChatMessageView> branch)
    {
        var turns = new List<List<ChatMessageView>>();
        foreach (var message in branch)
        {
            if (message.Role == "User" || turns.Count == 0) turns.Add([]);
            turns[^1].Add(message);
        }
        return turns;
    }

    private sealed class Accumulator(DateTimeOffset at)
    {
        public int Requests { get; private set; } = 1;

        public DateTimeOffset FirstAt { get; private set; } = at;

        public DateTimeOffset LastAt { get; private set; } = at;

        public void Add(DateTimeOffset other)
        {
            Requests++;
            if (other < FirstAt) FirstAt = other;
            if (other > LastAt) LastAt = other;
        }
    }
}
