namespace AI.Web.Widgets;

using AI.Contracts.Chats;
using AI.Contracts.Usage;
using AI.Web.Usage;

/// <summary>
/// Wall-clock and provider-reported time for the chat or last turn, in a shape the
/// <c>ChatPerformanceWidget</c> can render directly.
/// </summary>
/// <param name="WallClockMs">
/// Time from the first user message of the scope to the latest message, computed from message
/// timestamps. Marked approximate: a long pause between two messages counts as idle, not as the
/// model thinking.
/// </param>
/// <param name="ActiveMs">
/// Sum of every request's provider-reported <c>DurationMs</c> in the scope; the time the model
/// was actually asked for an answer. Null when no request was timed.
/// </param>
/// <param name="OutputSpeedTokensPerSecond">
/// <see cref="IUsagePresentation.OutputSpeed"/> over the scope's totals, or null when too little
/// was measured to say.
/// </param>
/// <param name="OutputTokens">Total output tokens of the scope.</param>
/// <param name="ReasoningTokens">Reasoning tokens of the scope.</param>
/// <param name="InputTokens">Input tokens of the scope.</param>
/// <param name="Requests">How many requests the scope covers.</param>
/// <param name="Turns">How many turns the scope covers.</param>
/// <param name="IsRunning">True while the last turn is still going.</param>
/// <param name="HasWallClock">
/// False when the scope has no user message at all — wall-clock and idle would be meaningless and
/// the widget says so in its empty state.
/// </param>
public sealed record PerformanceStatistics(
    long WallClockMs,
    long? ActiveMs,
    int? OutputSpeedTokensPerSecond,
    long OutputTokens,
    long ReasoningTokens,
    long InputTokens,
    int Requests,
    int Turns,
    bool IsRunning,
    bool HasWallClock)
{
    public long IdleMs => HasWallClock ? Math.Max(0, WallClockMs - (ActiveMs ?? 0)) : 0;

    public static PerformanceStatistics Empty { get; } =
        new(0, null, null, 0, 0, 0, 0, 0, false, false);
}

/// <summary>
/// Builds a <see cref="PerformanceStatistics"/> for the visible branch and the chosen scope.
/// Provider-reported figures come from <paramref name="chatUsage"/> (whole chat) or
/// <paramref name="liveTurn"/> (last turn); wall-clock and idle are worked out from message
/// timestamps and so are flagged approximate.
/// </summary>
public interface IChatPerformanceCalculator
{
    PerformanceStatistics Calculate(
        ChatTokenUsage? chatUsage,
        TurnTokenUsage? liveTurn,
        IReadOnlyList<ChatMessageView> branch,
        bool isRunning,
        ChatWidgetScope scope);
}

public sealed class ChatPerformanceCalculator(IUsagePresentation usage) : IChatPerformanceCalculator
{
    public PerformanceStatistics Calculate(
        ChatTokenUsage? chatUsage,
        TurnTokenUsage? liveTurn,
        IReadOnlyList<ChatMessageView> branch,
        bool isRunning,
        ChatWidgetScope scope)
    {
        if (branch.Count == 0) return PerformanceStatistics.Empty;
        var turns = SplitTurns(branch);
        if (turns.Count == 0) return PerformanceStatistics.Empty;

        var scopedTurns = scope == ChatWidgetScope.LastTurn && turns.Count > 1 ? turns[^1..] : turns;
        var totals = ResolveTotals(chatUsage, liveTurn, scope);
        var outputSpeed = totals.Requests > 0 ? usage.OutputSpeed(totals) : null;
        var wallClockMs = ComputeWallClockMs(scopedTurns);

        return new PerformanceStatistics(
            WallClockMs: wallClockMs,
            ActiveMs: totals.DurationMs > 0 ? totals.DurationMs : null,
            OutputSpeedTokensPerSecond: outputSpeed,
            OutputTokens: totals.Tokens.OutputTokens,
            ReasoningTokens: totals.Tokens.ReasoningTokens,
            InputTokens: totals.Tokens.InputTokens,
            Requests: totals.Requests,
            Turns: scopedTurns.Count,
            IsRunning: isRunning,
            HasWallClock: wallClockMs > 0);
    }

    // The same rule ChatUsageWidget uses: a live turn replaces the stored one when its request
    // count is at least as high, or when it started no earlier than the stored one. Empty totals
    // are returned when the chosen scope has no usage at all.
    private static TokenUsageTotals ResolveTotals(
        ChatTokenUsage? chatUsage,
        TurnTokenUsage? liveTurn,
        ChatWidgetScope scope)
    {
        if (scope == ChatWidgetScope.LastTurn)
        {
            var turn = ChooseTurn(chatUsage, liveTurn);
            return turn?.Totals ?? new TokenUsageTotals(new TokenCounts(0, 0), 0, 0, null, 0, 0);
        }
        return chatUsage?.Totals ?? new TokenUsageTotals(new TokenCounts(0, 0), 0, 0, null, 0, 0);
    }

    private static TurnTokenUsage? ChooseTurn(ChatTokenUsage? chatUsage, TurnTokenUsage? liveTurn)
    {
        var stored = chatUsage is { Turns.Count: > 0 } ? chatUsage.Turns[^1] : null;
        if (liveTurn is null) return stored;
        if (stored is null) return liveTurn;
        if (stored.TurnId == liveTurn.TurnId && liveTurn.Totals.Requests >= stored.Totals.Requests) return liveTurn;
        if (stored.TurnId != liveTurn.TurnId && liveTurn.StartedAt >= stored.StartedAt) return liveTurn;
        return stored;
    }

    private static long ComputeWallClockMs(IReadOnlyList<List<ChatMessageView>> turns)
    {
        var firstUser = turns.SelectMany(turn => turn)
            .FirstOrDefault(message => message.Role == "User");
        if (firstUser is null) return 0;
        var last = turns[^1].Count > 0 ? turns[^1][^1] : firstUser;
        return Math.Max(0, (long)(last.CreatedAt - firstUser.CreatedAt).TotalMilliseconds);
    }

    // A turn starts at a user message; whatever comes before the first one belongs to it.
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
}
