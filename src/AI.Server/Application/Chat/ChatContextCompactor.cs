namespace AI.Application.Chat;

using System.Text;

/// <summary>
/// Deterministically reduces model input. Tool results are projected to bounded excerpts first;
/// older user turns are then replaced as whole protocol groups by one synthetic summary.
/// </summary>
public sealed class ChatContextCompactor(IContextTokenEstimator estimator, IContextSummaryWriter summaryWriter) : IChatContextCompactor
{
    private const int RecentTurnsToKeep = 2;
    private static readonly (int Head, int Tail)[] ToolProjectionLimits =
        [(3_000, 1_000), (1_500, 500), (750, 250), (384, 128)];
    private const int SummaryUserCharacters = 600;
    private const int SummaryAssistantCharacters = 800;
    private const int SummaryLimitCharacters = 6_000;
    private const string ToolCompactionMarker = "[Tool result compacted for model context.";
    private static readonly int[] SummaryCharacterBudgets = [int.MaxValue, 2_000, 800, 300];

    public ContextCompactionResult Compact(IReadOnlyList<ChatCompletionMessage> messages, long inputLimit)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return CompactBest(messages, inputLimit).Result;
    }

    public async Task<ContextCompactionResult> CompactWithLlmAsync(
        IReadOnlyList<ChatCompletionMessage> messages,
        long inputLimit,
        int targetTokens,
        IContextSummarizer summarizer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(summarizer);
        var attempt = CompactBest(messages, inputLimit);
        if (estimator.EstimateMessages(attempt.Result.Messages) <= inputLimit) return attempt.Result;

        // The result is assembled from the deterministically projected messages, never from the
        // originals: rebuilding from the originals would restore every full tool result and can
        // inflate the request far above the limit the deterministic step already met.
        var (preamble, turns) = GroupTurns(attempt.Projected);
        if (turns.Count == 0) return attempt.Result;
        var keptTurns = Math.Clamp(attempt.KeptTurnCount, 1, turns.Count);
        var coveredTurnCount = turns.Count - keptTurns;

        ChatCompletionMessage? summaryMessage = null;
        ContextHistorySummary? kept = null;
        if (coveredTurnCount > 0)
        {
            // Summarize the original (unprojected) older turns so the summary sees full detail,
            // while the assembled context below still uses the projected messages.
            var (_, originalTurns) = GroupTurns(messages);
            var summarySource = (originalTurns.Count == turns.Count
                ? originalTurns.Take(coveredTurnCount)
                : turns.Take(coveredTurnCount)).SelectMany(turn => turn).ToArray();
            var summary = await summaryWriter.WriteAsync(summarySource, targetTokens, summarizer, cancellationToken);
            // The last stored message the summary covers is what lets it be kept: without one it
            // describes messages that exist only in this request, and stays in this request.
            var upTo = summarySource.LastOrDefault(message => message.MessageId is not null)?.MessageId;
            if (summary is not null)
            {
                summaryMessage = new ChatCompletionMessage("user", HistoryCheckpointService.SummaryPrefix + summary.Text,
                    MessageId: upTo);
                if (upTo is { } id)
                    kept = new ContextHistorySummary(summary.Text, id, summarySource.Length, summary.SourceCharacters);
            }
        }

        if (summaryMessage is null) coveredTurnCount = 0;
        return Assemble(preamble, summaryMessage, coveredTurnCount, turns, keptTurns, inputLimit) with { Summary = kept };
    }

    /// <summary>
    /// Builds the final message list and verifies it against the limit, dropping further turns and
    /// finally trimming the current turn until the request fits.
    /// </summary>
    private ContextCompactionResult Assemble(
        IReadOnlyList<ChatCompletionMessage> preamble,
        ChatCompletionMessage? summary,
        int coveredTurnCount,
        IReadOnlyList<IReadOnlyList<ChatCompletionMessage>> turns,
        int keptTurns,
        long inputLimit)
    {
        for (var keep = keptTurns; keep >= 1; keep--)
        {
            var omittedTurnCount = turns.Count - keep;
            var compacted = new List<ChatCompletionMessage>(preamble);
            if (summary is not null) compacted.Add(summary);
            var extra = turns.Skip(coveredTurnCount).Take(omittedTurnCount - coveredTurnCount).ToArray();
            if (extra.Length > 0) compacted.Add(BuildSummary(extra));
            foreach (var turn in turns.Skip(omittedTurnCount)) compacted.AddRange(turn);

            var omittedMessages = turns.Take(omittedTurnCount).Sum(turn => turn.Count);
            if (estimator.EstimateMessages(compacted) <= inputLimit)
                return new ContextCompactionResult(compacted, omittedMessages, true);
            if (keep > 1) continue;

            // Only the current turn is left: trim its completed head, tightening the summaries
            // themselves when even that is not enough.
            var last = turns[^1];
            ContextCompactionResult? smallest = null;
            foreach (var budget in SummaryCharacterBudgets)
            {
                var prefix = new List<ChatCompletionMessage>(preamble);
                if (summary is not null) prefix.Add(Truncate(summary, budget));
                if (extra.Length > 0) prefix.Add(Truncate(BuildSummary(extra), budget));
                smallest = TrimCurrentTurn(prefix, last, omittedMessages, inputLimit);
                if (estimator.EstimateMessages(smallest.Messages) <= inputLimit) break;
            }

            return smallest!;
        }

        return new ContextCompactionResult(preamble, 0, true);
    }

    /// <summary>
    /// Last resort: replaces the completed head of the current turn by a deterministic digest.
    /// Cuts are only taken before a non-tool message so tool calls keep their results. The size
    /// falls monotonically as the cut moves forward, so the smallest fitting cut is found by
    /// bisection instead of by estimating every candidate.
    /// </summary>
    private ContextCompactionResult TrimCurrentTurn(
        IReadOnlyList<ChatCompletionMessage> prefix,
        IReadOnlyList<ChatCompletionMessage> turn,
        int omittedBefore,
        long inputLimit)
    {
        var cuts = Enumerable.Range(1, Math.Max(0, turn.Count - 1))
            .Where(index => turn[index].Role != "tool")
            .Append(turn.Count)
            .ToArray();
        var low = 0;
        var high = cuts.Length - 1;
        var best = cuts.Length - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (estimator.EstimateMessages(BuildTrimmed(prefix, turn, cuts[middle])) <= inputLimit)
            {
                best = middle;
                high = middle - 1;
            }
            else
            {
                low = middle + 1;
            }
        }

        var cut = cuts[best];
        return new ContextCompactionResult(BuildTrimmed(prefix, turn, cut), omittedBefore + cut - 1, true);
    }

    private static List<ChatCompletionMessage> BuildTrimmed(
        IReadOnlyList<ChatCompletionMessage> prefix,
        IReadOnlyList<ChatCompletionMessage> turn,
        int cut)
    {
        var trimmed = new List<ChatCompletionMessage>(prefix) { turn[0], BuildTurnDigest(turn, cut) };
        trimmed.AddRange(turn.Skip(cut));
        return trimmed;
    }

    private static ChatCompletionMessage Truncate(ChatCompletionMessage message, int characters) =>
        message.Content.Length <= characters
            ? message
            : new ChatCompletionMessage(message.Role, message.Content[..characters] + "…");

    private static ChatCompletionMessage BuildTurnDigest(IReadOnlyList<ChatCompletionMessage> turn, int cut)
    {
        var digest = new StringBuilder("Current turn progress summary (deterministic; ")
            .Append(cut - 1)
            .Append(" earlier messages of this turn omitted):");
        var assistant = turn.Take(cut).LastOrDefault(message => message.Role == "assistant"
            && !string.IsNullOrWhiteSpace(message.ForModel));
        if (assistant is not null)
            Append(digest, "\n  Last assistant note: ", assistant.ForModel, SummaryAssistantCharacters);
        var tools = turn.Take(cut).SelectMany(message => message.ToolCalls ?? []).Select(call => call.Name)
            .Distinct(StringComparer.Ordinal).Take(20).ToArray();
        if (tools.Length > 0) digest.Append("\n  Tools used: ").Append(string.Join(", ", tools));
        return new ChatCompletionMessage("user", digest.ToString());
    }

    private CompactionAttempt CompactBest(IReadOnlyList<ChatCompletionMessage> messages, long inputLimit)
    {
        CompactionAttempt? smallest = null;
        foreach (var (head, tail) in ToolProjectionLimits)
        {
            var attempt = CompactOnce(messages, inputLimit, head, tail);
            smallest = attempt;
            if (estimator.EstimateMessages(attempt.Result.Messages) <= inputLimit) return attempt;
        }

        return smallest!;
    }

    private CompactionAttempt CompactOnce(IReadOnlyList<ChatCompletionMessage> messages, long inputLimit,
        int toolHeadCharacters, int toolTailCharacters)
    {
        var projected = ProjectLargeToolResults(messages, toolHeadCharacters, toolTailCharacters);
        var changedProjection = !ReferenceEquals(projected, messages);
        if (estimator.EstimateMessages(projected) <= inputLimit)
            return new CompactionAttempt(projected,
                new ContextCompactionResult(projected, 0, changedProjection), int.MaxValue);

        var (preamble, turns) = GroupTurns(projected);
        if (turns.Count <= 1)
            return new CompactionAttempt(projected,
                new ContextCompactionResult(projected, 0, changedProjection), turns.Count);

        var omittedTurnCount = Math.Max(0, turns.Count - RecentTurnsToKeep);
        while (omittedTurnCount < turns.Count)
        {
            var omitted = turns.Take(omittedTurnCount).SelectMany(turn => turn).ToArray();
            var compacted = new List<ChatCompletionMessage>(preamble.Count + 1
                + turns.Skip(omittedTurnCount).Sum(turn => turn.Count));
            compacted.AddRange(preamble);
            if (omitted.Length > 0) compacted.Add(BuildSummary(turns.Take(omittedTurnCount).ToArray()));
            foreach (var turn in turns.Skip(omittedTurnCount)) compacted.AddRange(turn);

            if (estimator.EstimateMessages(compacted) <= inputLimit || omittedTurnCount == turns.Count - 1)
                return new CompactionAttempt(projected,
                    new ContextCompactionResult(compacted, omitted.Length, true), turns.Count - omittedTurnCount);
            omittedTurnCount++;
        }

        return new CompactionAttempt(projected,
            new ContextCompactionResult(projected, 0, changedProjection), turns.Count);
    }

    private static IReadOnlyList<ChatCompletionMessage> ProjectLargeToolResults(
        IReadOnlyList<ChatCompletionMessage> messages,
        int toolHeadCharacters,
        int toolTailCharacters)
    {
        List<ChatCompletionMessage>? projected = null;
        var toolNames = messages.SelectMany(message => message.ToolCalls ?? [])
            .GroupBy(call => call.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);

        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            var modelContent = message.ForModel;
            if (message.Role != "tool" || modelContent.Length <= toolHeadCharacters + toolTailCharacters
                || modelContent.StartsWith(ToolCompactionMarker, StringComparison.Ordinal))
            {
                projected?.Add(message);
                continue;
            }

            projected ??= messages.Take(index).ToList();
            var toolName = message.ToolCallId is { } callId && toolNames.TryGetValue(callId, out var name)
                ? name
                : "unknown";
            var compacted = $"{ToolCompactionMarker} Tool: {toolName}. Original characters: {modelContent.Length}. "
                + "The beginning and end are retained. Re-run the tool or read the resource again if details are needed.]\n"
                + modelContent[..toolHeadCharacters]
                + "\n[...omitted...]\n"
                + modelContent[^toolTailCharacters..];
            projected.Add(message with { ModelContent = compacted });
        }

        return projected ?? messages;
    }

    private static (IReadOnlyList<ChatCompletionMessage> Preamble, IReadOnlyList<IReadOnlyList<ChatCompletionMessage>> Turns)
        GroupTurns(IReadOnlyList<ChatCompletionMessage> messages)
    {
        var preamble = new List<ChatCompletionMessage>();
        var turns = new List<IReadOnlyList<ChatCompletionMessage>>();
        List<ChatCompletionMessage>? current = null;
        foreach (var message in messages)
        {
            if (message.Role == "user")
            {
                if (current is { Count: > 0 }) turns.Add(current);
                current = [message];
            }
            else if (current is null)
            {
                preamble.Add(message);
            }
            else
            {
                current.Add(message);
            }
        }

        if (current is { Count: > 0 }) turns.Add(current);
        return (preamble, turns);
    }

    private static ChatCompletionMessage BuildSummary(IReadOnlyList<IReadOnlyList<ChatCompletionMessage>> turns)
    {
        var summary = new StringBuilder("Earlier conversation summary (deterministic; detailed messages omitted):");
        foreach (var turn in turns)
        {
            if (summary.Length >= SummaryLimitCharacters) break;
            var user = turn.FirstOrDefault(message => message.Role == "user");
            if (user is not null) Append(summary, "\n- User request: ", user.ForModel, SummaryUserCharacters);
            var assistant = turn.LastOrDefault(message => message.Role == "assistant"
                && message.ToolCalls is not { Count: > 0 } && !string.IsNullOrWhiteSpace(message.ForModel));
            if (assistant is not null) Append(summary, "\n  Assistant outcome: ", assistant.ForModel, SummaryAssistantCharacters);
            var tools = turn.SelectMany(message => message.ToolCalls ?? []).Select(call => call.Name)
                .Distinct(StringComparer.Ordinal).Take(10).ToArray();
            if (tools.Length > 0) summary.Append("\n  Tools used: ").Append(string.Join(", ", tools));
        }

        if (summary.Length > SummaryLimitCharacters)
            summary.Length = SummaryLimitCharacters;
        // The summary contains excerpts of user-authored text. Keeping the user role avoids
        // promoting untrusted instructions to system authority during compaction.
        return new ChatCompletionMessage("user", summary.ToString());
    }

    private static void Append(StringBuilder target, string label, string value, int limit)
    {
        target.Append(label);
        var normalized = value.ReplaceLineEndings(" ").Trim();
        if (normalized.Length <= limit) target.Append(normalized);
        else target.Append(normalized.AsSpan(0, limit)).Append('…');
    }

    /// <summary>One deterministic pass: the projected messages, its result and the turns it kept.</summary>
    private sealed record CompactionAttempt(
        IReadOnlyList<ChatCompletionMessage> Projected,
        ContextCompactionResult Result,
        int KeptTurnCount);
}
