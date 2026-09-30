namespace AI.Web.Usage;

using System.Collections.Concurrent;
using AI.Contracts.Usage;

public sealed class ChatUsageStore(IChatUsageApi api) : IChatUsageStore
{
    private readonly ConcurrentDictionary<Guid, ChatTokenUsage> _chats = new();

    public ChatTokenUsage? Find(Guid chatId) => _chats.GetValueOrDefault(chatId);

    public async Task<bool> RefreshAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        ChatTokenUsage? usage;
        try
        {
            usage = await api.GetChatAsync(projectId, chatId, cancellationToken);
        }
        catch (HttpRequestException)
        {
            // An unreachable Host keeps the last figures on screen; the run stream reports the outage.
            return false;
        }

        if (usage is null) return false;
        _chats[chatId] = usage;
        return true;
    }
}
