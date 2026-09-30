namespace AI.Web.Usage;

using AI.Contracts.Usage;

public interface IChatUsageApi
{
    Task<ChatTokenUsage?> GetChatAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);
}
