using AI.Client.Contracts.Runs;

namespace AI.Client.Web.Runs;

public interface IChatRunsApi
{
    Task<IReadOnlyList<ChatRunSnapshot>> GetAsync(CancellationToken cancellationToken);
    Task<ChatRunSnapshot> EnqueueAsync(Guid projectId, Guid chatId, EnqueueChatMessageRequest request, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> StopAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> MarkReadAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> UpdateQueuedAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, UpdateQueuedMessageRequest request, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> RemoveQueuedAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> ResumeAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> ClearAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
}
