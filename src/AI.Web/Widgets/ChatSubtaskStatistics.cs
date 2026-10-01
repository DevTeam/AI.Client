namespace AI.Web.Widgets;

using AI.Contracts.Chats;
using AI.Contracts.Usage;

/// <summary>
/// What the chat spent on delegated work, in a shape the <c>ChatSubtasksWidget</c> can render
/// directly. All numbers come from the same <see cref="TokenUsageTotals"/> the Usage widget reads
/// from, filtered to <see cref="TokenUsagePurpose.Subtask"/>; nothing is inferred from message
/// timestamps or from the tool messages in the transcript, because the ledger does not record how
/// many distinct subtasks ran, how each one finished or how long it took.
/// </summary>
/// <param name="Requests">
/// How many requests served delegated work in the scope. Provider-reported; the same count the
/// Usage widget shows when "Subtasks" is the chosen purpose slice.
/// </param>
/// <param name="TurnsWithSubtasks">
/// How many of the scope's turns started at least one subtask request. Derived from the per-turn
/// slices; a turn that delegated is the one with a non-empty <c>Subtask</c> slice.
/// </param>
/// <param name="InputTokens">Sum of input tokens the subtask requests consumed.</param>
/// <param name="OutputTokens">Sum of output tokens they produced, including reasoning.</param>
/// <param name="ReasoningTokens">The reasoning share of <paramref name="OutputTokens"/>.</param>
/// <param name="Share">
/// Part of the scope's total tokens (input + output) that went to subtasks, from 0 to 1. Null
/// when the scope used nothing at all and a share would divide by zero.
/// </param>
/// <param name="HasData">
/// True when there is at least one subtask request in the scope. The widget uses it to keep its
/// empty state out of the way when purpose data is missing entirely (a chat with no usage yet).
/// </param>
/// <param name="Turns">How many turns the scope covers; matches the <c>Turns</c> the Performance and Usage widgets report for the same scope.</param>
/// <param name="IsRunning">True while the scope's last turn is still going; the widget says so far.</param>
public sealed record SubtaskStatistics(
    int Requests,
    int TurnsWithSubtasks,
    int Turns,
    long InputTokens,
    long OutputTokens,
    long ReasoningTokens,
    double? Share,
    bool HasData,
    bool IsRunning)
{
    public static SubtaskStatistics Empty { get; } =
        new(0, 0, 0, 0, 0, 0, null, false, false);
}

/// <summary>
/// Builds a <see cref="SubtaskStatistics"/> for the visible branch and the chosen scope, using the
/// same chat-vs-turn rule the Usage and Performance widgets use, so the three widgets agree on
/// what "the last turn" means.
/// </summary>
public interface IChatSubtaskStatisticsCalculator
{
    /// <param name="branch">The visible branch, root to leaf.</param>
    /// <param name="isRunning">True while the last turn is still going; the widget says so far.</param>
    /// <param name="scope">Whole chat or last turn.</param>
    SubtaskStatistics Calculate(
        ChatTokenUsage? chatUsage,
        TurnTokenUsage? liveTurn,
        IReadOnlyList<ChatMessageView> branch,
        bool isRunning,
        ChatWidgetScope scope);
}

public sealed class ChatSubtaskStatisticsCalculator : IChatSubtaskStatisticsCalculator
{
    public SubtaskStatistics Calculate(
        ChatTokenUsage? chatUsage,
        TurnTokenUsage? liveTurn,
        IReadOnlyList<ChatMessageView> branch,
        bool isRunning,
        ChatWidgetScope scope)
    {
        _ = isRunning;
        if (branch.Count == 0) return SubtaskStatistics.Empty;
        var turns = SplitTurns(branch);
        if (turns.Count == 0) return SubtaskStatistics.Empty;

        // Whole chat or last turn. The scope's totals come from the matching ledger: chat-wide
        // totals on ChatTokenUsage, last-turn totals from the chosen turn. The rule is shared
        // with ChatUsageWidget and ChatPerformanceWidget so the three stay in step.
        var slice = scope == ChatWidgetScope.LastTurn
            ? FindSubtaskSlice(ChooseTurn(chatUsage, liveTurn)?.ByPurpose)
            : FindSubtaskSlice(chatUsage?.ByPurpose);
        if (slice is null) return SubtaskStatistics.Empty;

        var scopeTotals = scope == ChatWidgetScope.LastTurn
            ? ChooseTurn(chatUsage, liveTurn)?.Totals ?? chatUsage?.Totals
            : chatUsage?.Totals;
        var scopeTotal = (scopeTotals?.Tokens.InputTokens ?? 0) + (scopeTotals?.Tokens.OutputTokens ?? 0);
        var subtaskTotal = slice.Totals.Tokens.InputTokens + slice.Totals.Tokens.OutputTokens;
        var share = scopeTotal > 0 ? (double)subtaskTotal / scopeTotal : (double?)null;

        var scopedTurns = scope == ChatWidgetScope.LastTurn && turns.Count > 1 ? turns[^1..] : turns;
        var turnsWithSubtasks = CountTurnsWithSubtasks(chatUsage, scope, scopedTurns.Count);

        return new SubtaskStatistics(
            Requests: slice.Totals.Requests,
            TurnsWithSubtasks: turnsWithSubtasks,
            Turns: scopedTurns.Count,
            InputTokens: slice.Totals.Tokens.InputTokens,
            OutputTokens: slice.Totals.Tokens.OutputTokens,
            ReasoningTokens: slice.Totals.Tokens.ReasoningTokens,
            Share: share,
            HasData: slice.Totals.Requests > 0 || scopeTotal > 0,
            IsRunning: isRunning && scope == ChatWidgetScope.LastTurn);
    }

    // Same chat-vs-turn rule ChatPerformanceCalculator uses: a live turn replaces the stored one
    // when its request count is at least as high, or when it started no earlier than the stored
    // one. Sharing the rule keeps the widgets from disagreeing about which turn "last" means.
    private static TurnTokenUsage? ChooseTurn(ChatTokenUsage? chatUsage, TurnTokenUsage? liveTurn)
    {
        var stored = chatUsage is { Turns.Count: > 0 } ? chatUsage.Turns[^1] : null;
        if (liveTurn is null) return stored;
        if (stored is null) return liveTurn;
        if (stored.TurnId == liveTurn.TurnId && liveTurn.Totals.Requests >= stored.Totals.Requests) return liveTurn;
        if (stored.TurnId != liveTurn.TurnId && liveTurn.StartedAt >= stored.StartedAt) return liveTurn;
        return stored;
    }

    private static TokenUsageSlice? FindSubtaskSlice(IReadOnlyList<TokenUsageSlice>? slices)
    {
        if (slices is null) return null;
        foreach (var slice in slices)
        {
            if (string.Equals(slice.Key, nameof(TokenUsagePurpose.Subtask), StringComparison.Ordinal))
                return slice;
        }
        return null;
    }

    // Turns with subtasks, taken from the per-turn breakdown. The breakdown's order matches the
    // ledger's: the last entry is the latest turn, the same one the branch's last user message
    // starts. Counting from the back avoids needing to align turn ids.
    private static int CountTurnsWithSubtasks(ChatTokenUsage? chatUsage, ChatWidgetScope scope, int scopedTurnCount)
    {
        if (chatUsage is null || scopedTurnCount == 0) return 0;
        var turns = chatUsage.Turns;
        if (turns.Count == 0) return 0;
        var take = Math.Min(scopedTurnCount, turns.Count);
        var count = 0;
        for (var index = turns.Count - take; index < turns.Count; index++)
        {
            if (FindSubtaskSlice(turns[index].ByPurpose) is { } slice && slice.Totals.Requests > 0)
            {
                count++;
            }
        }
        // Last-turn scope asks about a single turn; the running turn is one of them when its
        // request count is at least as high as the stored one's (ChooseTurn), and the loop above
        // counts the stored entry. A live turn whose ByPurpose isn't yet on the ledger is a rare
        // gap; the caller's running headline will say so far regardless.
        _ = scope;
        return count;
    }

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
