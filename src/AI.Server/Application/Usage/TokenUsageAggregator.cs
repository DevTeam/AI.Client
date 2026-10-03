namespace AI.Application.Usage;

using AI.Contracts.Usage;
using System.Globalization;

public sealed class TokenUsageAggregator : ITokenUsageAggregator
{
    private static readonly TokenUsageTotals Zero = new(new TokenCounts(0, 0), 0, 0, null, 0, 0);

    public TokenUsageTotals Total(IEnumerable<TokenUsageRecord> records) => records.Aggregate(Zero, Add);

    public TokenUsageTotals Add(TokenUsageTotals totals, TokenUsageRecord record) => new(
        new TokenCounts(
            totals.Tokens.InputTokens + record.Tokens.InputTokens,
            totals.Tokens.OutputTokens + record.Tokens.OutputTokens,
            totals.Tokens.CachedInputTokens + record.Tokens.CachedInputTokens,
            totals.Tokens.ReasoningTokens + record.Tokens.ReasoningTokens),
        totals.Requests + 1,
        totals.EstimatedRequests + (record.Estimated ? 1 : 0),
        record.Cost is { } cost ? (totals.Cost ?? 0) + cost : totals.Cost,
        totals.PricedRequests + (record.Cost is null ? 0 : 1),
        totals.DurationMs + record.DurationMs,
        totals.EstimatedCostRequests + (record is { Cost: not null, CostEstimated: true } ? 1 : 0),
        totals.ReusableInputTokens + (record.Prefix is { EstimatedInputTokens: > 0 } prefix
            ? Math.Clamp(prefix.ReusableTokens, 0, prefix.EstimatedInputTokens) : 0),
        totals.ToolChanges + (record.Prefix?.Change == PromptPrefixChange.Tools ? 1 : 0),
        totals.InstructionChanges + (record.Prefix?.Change == PromptPrefixChange.Instructions ? 1 : 0),
        totals.HistoryChanges + (record.Prefix?.Change == PromptPrefixChange.History ? 1 : 0),
        totals.PrefixInputTokens + Math.Max(0, record.Prefix?.EstimatedInputTokens ?? 0));

    /// <summary>Groups, largest first: the question a breakdown answers is where most of it went.</summary>
    public IReadOnlyList<TokenUsageSlice> Group(IEnumerable<TokenUsageRecord> records, Func<TokenUsageRecord, string> key) =>
        records.GroupBy(key, StringComparer.Ordinal)
            .Select(group => new TokenUsageSlice(group.Key, Total(group)))
            .OrderByDescending(slice => slice.Totals.Tokens.InputTokens + slice.Totals.Tokens.OutputTokens)
            .ThenBy(slice => slice.Key, StringComparer.Ordinal)
            .ToArray();

    public ChatTokenUsage Chat(Guid projectId, Guid chatId, IEnumerable<TokenUsageRecord> records)
    {
        var own = records.Where(record => record.ProjectId == projectId && record.ChatId == chatId).ToArray();
        var turns = own.Where(record => record.TurnId is not null)
            .GroupBy(record => record.TurnId!.Value)
            .Select(turn => new TurnTokenUsage(turn.Key, turn.First().BranchId ?? chatId, turn.Min(record => record.At),
                Total(turn), Group(turn, Purpose), AnswerModels(turn)))
            .OrderBy(turn => turn.StartedAt)
            .ToArray();
        return new ChatTokenUsage(projectId, chatId, Total(own), Group(own, Purpose), turns);
    }

    public TokenUsageReport Report(DateTimeOffset from, DateTimeOffset until, IEnumerable<TokenUsageRecord> records)
    {
        var inRange = records.Where(record => record.At >= from && record.At < until).ToArray();
        return new TokenUsageReport(from, until, Total(inRange),
            Group(inRange, record => record.At.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).OrderBy(day => day.Key, StringComparer.Ordinal).ToArray(),
            Group(inRange, record => record.Model),
            Group(inRange, Purpose));
    }

    private static string Purpose(TokenUsageRecord record) => record.Purpose.ToString();

    public IReadOnlyList<AnswerModelUsage> AnswerModels(IEnumerable<TokenUsageRecord> records) =>
        records.Where(record => record.Purpose == TokenUsagePurpose.Answer && !string.IsNullOrWhiteSpace(record.Model))
            .OrderBy(record => record.At)
            .Select(record => new AnswerModelUsage(record.Id, record.At, record.Model.Trim()))
            .ToArray();
}
