namespace AI.Client.Application.Chat;

using System.Text;
using Contracts.Chat;

/// <summary>
/// Deterministically reduces model input. Tool results are projected to bounded excerpts first;
/// older user turns are then replaced as whole protocol groups by one synthetic summary.
/// </summary>
public sealed class ChatContextCompactor(IContextTokenEstimator estimator) : IChatContextCompactor
{
    private const int RecentTurnsToKeep = 2;
    private const int ToolHeadCharacters = 3_000;
    private const int ToolTailCharacters = 1_000;
    private const int SummaryUserCharacters = 600;
    private const int SummaryAssistantCharacters = 800;
    private const int SummaryLimitCharacters = 6_000;
    private const string ToolCompactionMarker = "[Tool result compacted for model context.";

    public ContextCompactionResult Compact(IReadOnlyList<ChatCompletionMessage> messages, long inputLimit)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var projected = ProjectLargeToolResults(messages);
        var changedProjection = !ReferenceEquals(projected, messages);
        if (estimator.EstimateMessages(projected) <= inputLimit)
            return new ContextCompactionResult(projected, 0, changedProjection);

        var (preamble, turns) = GroupTurns(projected);
        if (turns.Count <= 1)
            return new ContextCompactionResult(projected, 0, changedProjection);

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
                return new ContextCompactionResult(compacted, omitted.Length, true);
            omittedTurnCount++;
        }

        return new ContextCompactionResult(projected, 0, changedProjection);
    }

    private static IReadOnlyList<ChatCompletionMessage> ProjectLargeToolResults(
        IReadOnlyList<ChatCompletionMessage> messages)
    {
        List<ChatCompletionMessage>? projected = null;
        var toolNames = messages.SelectMany(message => message.ToolCalls ?? [])
            .GroupBy(call => call.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);

        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            var modelContent = message.ForModel;
            if (message.Role != "tool" || modelContent.Length <= ToolHeadCharacters + ToolTailCharacters
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
                + modelContent[..ToolHeadCharacters]
                + "\n[...omitted...]\n"
                + modelContent[^ToolTailCharacters..];
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
}
