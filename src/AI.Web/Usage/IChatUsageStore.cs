namespace AI.Web.Usage;

using AI.Contracts.Chats;
using AI.Contracts.Usage;

/// <summary>
/// What the Host's ledger says each chat has used, and which summaries stand in for its history,
/// as last read. The current turn is not waited for: it arrives live on the run snapshot, and the
/// stored copy is read again once it ends.
/// </summary>
public interface IChatUsageStore
{
    ChatTokenUsage? Find(Guid chatId);

    IReadOnlyList<HistoryCheckpoint> CheckpointsOf(Guid chatId);

    /// <summary>Reads the chat again; false when the Host could not be reached or refused.</summary>
    Task<bool> RefreshAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);
}
