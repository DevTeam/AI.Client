namespace AI.Application.Chats;

using AI.Contracts.Chats;
using AI.Domain.Chats;
using AI.Domain.Projects;

public interface IChatRepository
{
    Task<IReadOnlyList<StoredChatSummary>> ListSummariesAsync(ProjectId projectId, CancellationToken cancellationToken);
    Task<StoredChat?> GetAsync(ProjectId projectId, ChatId id, CancellationToken cancellationToken);
    /// <summary>Null means the chat is absent; a result with null Message means only the message is absent.</summary>
    Task<ChatMessageLookup?> GetMessageAsync(ProjectId projectId, ChatId id, ChatMessageId messageId,
        CancellationToken cancellationToken);
    /// <summary>False means the document definitely has no occurrence of the review ID.</summary>
    Task<bool?> MayContainReviewReferenceAsync(ProjectId projectId, ChatId id, Guid reviewId,
        CancellationToken cancellationToken);
    Task<ChatSaveResult> SaveAsync(ChatThread chat, long expectedRevision, CancellationToken cancellationToken);
    Task<ChatDeleteResult> DeleteAsync(ProjectId projectId, ChatId id, long expectedRevision, CancellationToken cancellationToken);
}

public interface IPersistentChatRepository : IChatRepository { }
public interface IHostLifetimeChatRepository : IChatRepository { }

public sealed record ChatMessageLookup(ChatMessage? Message);
