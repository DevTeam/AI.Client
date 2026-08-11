using AI.Client.Contracts.Chats;
using AI.Client.Domain.Chats;
using AI.Client.Domain.Projects;

namespace AI.Client.Application.Chats;

public interface IChatRepository
{
    Task<IReadOnlyList<StoredChat>> ListAsync(ProjectId projectId, CancellationToken cancellationToken);
    Task<StoredChat?> GetAsync(ProjectId projectId, ChatId id, CancellationToken cancellationToken);
    Task<ChatSaveResult> SaveAsync(ChatThread chat, long expectedRevision, CancellationToken cancellationToken);
    Task<ChatDeleteResult> DeleteAsync(ProjectId projectId, ChatId id, long expectedRevision, CancellationToken cancellationToken);
}
