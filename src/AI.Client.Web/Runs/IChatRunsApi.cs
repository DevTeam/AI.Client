namespace AI.Client.Web.Runs;

using AI.Client.Contracts.Runs;

public interface IChatRunsApi
{
    Task DecideToolAsync(Guid projectId, Guid chatId, Guid branchId, ToolApprovalDecision decision, CancellationToken cancellationToken);
    Task<ChatRunSnapshot> SubmitAsync(Guid projectId, Guid chatId, SubmitChatMessageRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatRunSnapshot>> GetAsync(CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> StopAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> MarkReadAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> UpdateQueuedAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, UpdateQueuedMessageRequest request, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> RemoveQueuedAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> ResumeAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> SkipFailedAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> RebaseAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> ClearAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> ClearAllAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> DiscardAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
}
