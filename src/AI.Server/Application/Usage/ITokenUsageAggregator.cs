namespace AI.Application.Usage;

using AI.Contracts.Usage;

/// <summary>Adds records up into the groups the usage views show.</summary>
public interface ITokenUsageAggregator
{
    TokenUsageTotals Total(IEnumerable<TokenUsageRecord> records);

    TokenUsageTotals Add(TokenUsageTotals totals, TokenUsageRecord record);

    IReadOnlyList<TokenUsageSlice> Group(IEnumerable<TokenUsageRecord> records, Func<TokenUsageRecord, string> key);

    ChatTokenUsage Chat(Guid projectId, Guid chatId, IEnumerable<TokenUsageRecord> records);

    TokenUsageReport Report(DateTimeOffset from, DateTimeOffset until, IEnumerable<TokenUsageRecord> records);
}
