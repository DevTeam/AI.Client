namespace AI.Client.Application.Chats;

using AI.Client.Contracts.Chats;

public interface IChatService
{
    Task<IReadOnlyList<ChatSummary>> ListAsync(Guid projectId, CancellationToken cancellationToken);
    Task<ChatDetails?> GetAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);
    Task<ChatDetails?> GetTranscriptAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);
    Task<ChatTurnActivity?> GetTurnActivityAsync(Guid projectId, Guid chatId, Guid turnId, Guid branchLeafId, CancellationToken cancellationToken);
    Task<ChatMessageContent?> GetMessageContentAsync(Guid projectId, Guid chatId, Guid messageId, CancellationToken cancellationToken);
    Task<ChatDetails?> RemoveReviewReferenceAsync(Guid projectId, Guid chatId, Guid messageId,
        Guid reviewId, long revision, CancellationToken cancellationToken);
    Task<ChatDetails?> RemoveResourceReferenceAsync(Guid projectId, Guid chatId, Guid messageId,
        Guid resourceId, long revision, CancellationToken cancellationToken);
    Task<ChatDetails> CreateAsync(Guid projectId, CreateChatRequest request, CancellationToken cancellationToken);
    Task<ChatDetails?> AppendMessageAsync(Guid projectId, Guid chatId, AppendChatMessageRequest request, CancellationToken cancellationToken);
    Task<ChatDetails?> UpdateEndpointAsync(Guid projectId, Guid chatId, UpdateChatEndpointRequest request, CancellationToken cancellationToken);
    Task<ChatDetails?> RenameAsync(Guid projectId, Guid chatId, RenameChatRequest request, CancellationToken cancellationToken);
    Task<ChatSummary?> PinAsync(Guid projectId, Guid chatId, PinChatRequest request, CancellationToken cancellationToken);
    Task<ChatDetails?> RenameBranchAsync(Guid projectId, Guid chatId, Guid branchId, RenameChatBranchRequest request, CancellationToken cancellationToken);
    Task<ChatDetails?> SetToolPolicyAsync(Guid projectId, Guid chatId, AI.Client.Contracts.Projects.ToolPolicySettings policy, CancellationToken cancellationToken);
    Task<ChatDetails?> RemoveToolPolicyAsync(Guid projectId, Guid chatId, Guid serverId, string name, string schemaHash, CancellationToken cancellationToken);
}
