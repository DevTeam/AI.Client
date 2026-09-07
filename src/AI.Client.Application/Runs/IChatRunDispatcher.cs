namespace AI.Client.Application.Runs;

using AI.Client.Contracts.Runs;
using AI.Client.Contracts.Chats;
using AI.Client.Contracts.Projects;

public interface IChatRunDispatcher
{
    Task<bool> DecideToolAsync(Guid projectId, Guid chatId, Guid branchId, ToolApprovalDecision decision, CancellationToken token);
    Task ShutdownAsync(CancellationToken cancellationToken);
    Task<ChatRunSnapshot> SubmitAsync(Guid projectId, Guid chatId, SubmitChatMessageRequest request, CancellationToken cancellationToken);
    Task WarmUpAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ChatRunSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<IReadOnlyList<ChatRunSnapshot>> SubscribeAsync(CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> StopAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null);
    Task<ChatRunSnapshot?> MarkReadAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null);
    Task<ChatRunSnapshot?> UpdateQueuedAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, UpdateQueuedMessageRequest request, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> RemoveQueuedAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, CancellationToken cancellationToken, Guid? operationId = null);
    Task<ChatRunSnapshot?> ResumeAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null);
    Task<ChatRunSnapshot?> ClearAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null);
    Task ReconcileChatAsync(Guid projectId, Guid chatId, IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken);
    Task<ChatDeleteResult> DeleteChatAsync(Guid projectId, Guid chatId, long revision, CancellationToken cancellationToken);
    Task<ChatBranchDeleteResult> DeleteBranchAsync(Guid projectId, Guid chatId, Guid branchId, long revision, CancellationToken cancellationToken);
    Task<ProjectDeleteResult> DeleteProjectAsync(Guid projectId, long revision, CancellationToken cancellationToken);
}
