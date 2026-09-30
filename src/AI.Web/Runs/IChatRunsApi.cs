namespace AI.Web.Runs;

using AI.Contracts.Runs;

public interface IChatRunsApi
{
    Task DecideToolAsync(Guid projectId, Guid chatId, Guid branchId, ToolApprovalDecision decision, CancellationToken cancellationToken);

    /// <summary>
    /// Answers the question a run is waiting on. A refused answer means the prompt moved on without
    /// it — the run stopped, the question expired, somebody answered in another window — which the
    /// card reports rather than retries.
    /// </summary>
    Task<bool> AnswerPromptAsync(Guid projectId, Guid chatId, Guid branchId, UserPromptResponse response, CancellationToken cancellationToken);
    Task<ChatRunSnapshot> SubmitAsync(Guid projectId, Guid chatId, SubmitChatMessageRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatRunSnapshot>> GetAsync(CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> StopAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> MarkReadAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> UpdateQueuedAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, UpdateQueuedMessageRequest request, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> RemoveQueuedAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> SendQueuedNowAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> ResumeAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> SkipFailedAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> RebaseAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> ClearAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> ClearAllAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
    /// <summary>
    /// The Host's draft of the user's reply to <paramref name="leafMessageId"/>, or null when there is
    /// none. Without <paramref name="generate"/> it only waits for a draft already being written.
    /// </summary>
    Task<ChatReplySuggestion?> GetReplySuggestionAsync(Guid projectId, Guid chatId, Guid branchId, Guid leafMessageId,
        bool generate, CancellationToken cancellationToken);
    Task<ChatRunSnapshot?> DiscardAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken);
}
