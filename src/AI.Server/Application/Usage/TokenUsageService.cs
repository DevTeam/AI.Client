namespace AI.Application.Usage;

using AI.Contracts.Usage;

public sealed class TokenUsageService(ITokenUsageLedger ledger, ITokenUsageAggregator aggregator) : ITokenUsageService
{
    public async Task<ChatTokenUsage> GetChatAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken) =>
        aggregator.Chat(projectId, chatId,
            await ledger.ReadAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, cancellationToken));

    public async Task<TokenUsageReport> GetReportAsync(DateTimeOffset from, DateTimeOffset until, Guid? projectId,
        CancellationToken cancellationToken)
    {
        if (until <= from) throw new ArgumentException("The end of the period must be after its start.");
        var records = await ledger.ReadAsync(from, until, cancellationToken);
        return aggregator.Report(from, until,
            projectId is { } project ? records.Where(record => record.ProjectId == project) : records);
    }
}
