using AI.Client.Contracts.Runs;

namespace AI.Client.Application.Runs;

public interface IChatRunDispatcher
{
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
