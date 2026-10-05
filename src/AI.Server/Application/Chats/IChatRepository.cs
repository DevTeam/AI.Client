namespace AI.Application.Chats;

using AI.Contracts.Chats;
using AI.Domain.Chats;
using AI.Domain.Projects;

public interface IChatRepository
{
    Task<IReadOnlyList<StoredChatSummary>> ListSummariesAsync(ProjectId projectId, CancellationToken cancellationToken);
    Task<StoredChat?> GetAsync(ProjectId projectId, ChatId id, CancellationToken cancellationToken);
    Task<ChatSaveResult> SaveAsync(ChatThread chat, long expectedRevision, CancellationToken cancellationToken);
    Task<ChatDeleteResult> DeleteAsync(ProjectId projectId, ChatId id, long expectedRevision, CancellationToken cancellationToken);
}

public interface IPersistentChatRepository : IChatRepository { }
public interface IHostLifetimeChatRepository : IChatRepository { }
