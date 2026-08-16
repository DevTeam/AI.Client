namespace AI.Client.Application.Chats;

using AI.Client.Contracts.Chats;

public interface IChatService
{
    Task<IReadOnlyList<ChatSummary>> ListAsync(Guid projectId, CancellationToken cancellationToken);
    Task<ChatDetails?> GetAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);
    Task<ChatDetails> CreateAsync(Guid projectId, CreateChatRequest request, CancellationToken cancellationToken);
    Task<ChatDetails?> AppendMessageAsync(Guid projectId, Guid chatId, AppendChatMessageRequest request, CancellationToken cancellationToken);
    Task<ChatDetails?> UpdateEndpointAsync(Guid projectId, Guid chatId, UpdateChatEndpointRequest request, CancellationToken cancellationToken);
    Task<ChatDetails?> RenameAsync(Guid projectId, Guid chatId, RenameChatRequest request, CancellationToken cancellationToken);
    Task<ChatDetails?> RenameBranchAsync(Guid projectId, Guid chatId, Guid branchId, RenameChatBranchRequest request, CancellationToken cancellationToken);
}
