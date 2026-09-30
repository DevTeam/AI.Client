namespace AI.Web.Usage;

using System.Collections.Concurrent;
using AI.Contracts.Chats;
using AI.Contracts.Usage;

public sealed class ChatUsageStore(IChatUsageApi api, IHistoryCheckpointApi checkpoints) : IChatUsageStore
{
    private readonly ConcurrentDictionary<Guid, ChatTokenUsage> _chats = new();
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<HistoryCheckpoint>> _checkpoints = new();

    public ChatTokenUsage? Find(Guid chatId) => _chats.GetValueOrDefault(chatId);

    public IReadOnlyList<HistoryCheckpoint> CheckpointsOf(Guid chatId) => _checkpoints.GetValueOrDefault(chatId) ?? [];

    public async Task<bool> RefreshAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        ChatTokenUsage? usage;
        IReadOnlyList<HistoryCheckpoint> kept;
        try
        {
            var usageRead = api.GetChatAsync(projectId, chatId, cancellationToken);
            var checkpointsRead = checkpoints.ListAsync(projectId, chatId, cancellationToken);
            usage = await usageRead;
            kept = await checkpointsRead;
        }
        catch (HttpRequestException)
        {
            // An unreachable Host keeps the last figures on screen; the run stream reports the outage.
            return false;
        }

        _checkpoints[chatId] = kept;
        if (usage is null) return false;
        _chats[chatId] = usage;
        return true;
    }
}
