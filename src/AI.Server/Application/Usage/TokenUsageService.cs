namespace AI.Application.Usage;

using AI.Contracts.Usage;

public sealed class TokenUsageService(ITokenUsageLedger ledger, ITokenUsageAggregator aggregator,
    IConnectionRateLimits rateLimits) : ITokenUsageService
{
    public async Task<ChatTokenUsage> GetChatAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        var records = await ledger.ReadAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, cancellationToken);
        var usage = aggregator.Chat(projectId, chatId, records);
        // The limits of the connection the chat last used, which is the one its next turn will meet.
        var connection = records.LastOrDefault(record => record.ProjectId == projectId && record.ChatId == chatId
                                                          && record.ConnectionId is not null)?.ConnectionId;
        return connection is { } id && rateLimits.Find(id) is { } limits ? usage with { RateLimits = limits } : usage;
    }

    public async Task<TokenUsageReport> GetReportAsync(DateTimeOffset from, DateTimeOffset until, Guid? projectId,
        CancellationToken cancellationToken)
    {
        if (until <= from) throw new ArgumentException("The end of the period must be after its start.");
        var records = await ledger.ReadAsync(from, until, cancellationToken);
        return aggregator.Report(from, until,
            projectId is { } project ? records.Where(record => record.ProjectId == project) : records);
    }
}
