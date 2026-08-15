using AI.Client.Contracts.Runs;

namespace AI.Client.Application.Runs;

public interface IChatRunDispatcher
{
    /// <summary>
    /// Loads every persisted run into the live in-memory set. Runtimes otherwise load lazily,
    /// one branch at a time, only when a mutating call (Enqueue/Stop/Resume/etc.) touches that
    /// branch — so right after a process restart, any branch nobody has interacted with yet is
    /// invisible to <see cref="SubscribeAsync"/> even though <see cref="GetSnapshotAsync"/> (which
    /// reads the persisted store directly) still reports it. That gap makes it look, to a client
    /// that reconciles its cache off the live stream, as if the run vanished. Call this once at
    /// startup so the stream and the snapshot endpoint agree from the first moment.
    /// </summary>
    Task WarmUpAsync(CancellationToken cancellationToken);

    Task<ChatRunSnapshot> EnqueueAsync(Guid projectId, Guid chatId, EnqueueChatMessageRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatRunSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<IReadOnlyList<ChatRunSnapshot>> SubscribeAsync(CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> StopAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> MarkReadAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> UpdateQueuedAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, UpdateQueuedMessageRequest request, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> RemoveQueuedAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> ResumeAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> ClearAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
    Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task DeleteChatAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);
    Task ReconcileChatAsync(Guid projectId, Guid chatId, IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken);
}
