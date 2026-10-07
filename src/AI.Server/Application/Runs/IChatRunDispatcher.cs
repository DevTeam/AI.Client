namespace AI.Application.Runs;

using AI.Contracts.Runs;
using AI.Contracts.Chats;
using AI.Contracts.Projects;

public interface IChatRunDispatcher
{
    bool TryEnterUpdateMaintenance();
    void LeaveUpdateMaintenance();
    Task<bool> DecideToolAsync(Guid projectId, Guid chatId, Guid branchId, ToolApprovalDecision decision, CancellationToken token);
    Task<bool> AnswerPromptAsync(Guid projectId, Guid chatId, Guid branchId, UserPromptResponse response, CancellationToken token);
    Task ShutdownAsync(CancellationToken cancellationToken);
    Task<ChatRunSnapshot> SubmitAsync(Guid projectId, Guid chatId, SubmitChatMessageRequest request, CancellationToken cancellationToken);
    /// <summary>Enqueues a run from a trusted host component without a person to answer prompts.</summary>
    Task<ChatRunSnapshot> SubmitUnattendedAsync(Guid projectId, Guid chatId, SubmitChatMessageRequest request,
        CancellationToken cancellationToken);
    /// <summary>
    /// Submits on behalf of a model run, which becomes the message's sender. Only the host sets a
    /// sender; the HTTP API never accepts one.
    /// </summary>
    Task<ChatRunSnapshot> SubmitFromRunAsync(Guid projectId, Guid chatId, SubmitChatMessageRequest request,
        AI.Domain.Chats.ChatMessageSender sender, CancellationToken cancellationToken);
    Task WarmUpAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ChatRunSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<IReadOnlyList<ChatRunSnapshot>> SubscribeAsync(CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> StopAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null);
    Task<ChatRunSnapshot?> MarkReadAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null);
    Task<ChatRunSnapshot?> UpdateQueuedAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, UpdateQueuedMessageRequest request, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> RemoveQueuedAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, CancellationToken cancellationToken, Guid? operationId = null);
    Task<ChatRunSnapshot?> SendQueuedNowAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, CancellationToken cancellationToken, Guid? operationId = null);
    Task<ChatRunSnapshot?> ResumeAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null);
    Task<ChatRunSnapshot?> SkipFailedAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null);
    Task<ChatRunSnapshot?> RebaseAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null);
    Task<ChatRunSnapshot?> ClearAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null);
    Task<ChatRunSnapshot?> ClearAllAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null);
    Task<ChatRunSnapshot?> DiscardAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null);
    Task ReconcileChatAsync(Guid projectId, Guid chatId, IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken);
    Task<ChatDeleteResult> DeleteChatAsync(Guid projectId, Guid chatId, long revision, CancellationToken cancellationToken);
    Task<ChatBranchDeleteResult> DeleteBranchAsync(Guid projectId, Guid chatId, Guid branchId, long revision, CancellationToken cancellationToken);
    Task<ProjectDeleteResult> DeleteProjectAsync(Guid projectId, long revision, CancellationToken cancellationToken);
}
