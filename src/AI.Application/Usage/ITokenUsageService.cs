namespace AI.Application.Usage;

using AI.Contracts.Usage;

public interface ITokenUsageService
{
    Task<ChatTokenUsage> GetChatAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);

    /// <param name="projectId">Limits the report to one project; null covers everything.</param>
    Task<TokenUsageReport> GetReportAsync(DateTimeOffset from, DateTimeOffset until, Guid? projectId,
        CancellationToken cancellationToken);
}
