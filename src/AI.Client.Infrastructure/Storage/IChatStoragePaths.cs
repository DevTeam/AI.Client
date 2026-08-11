using AI.Client.Domain.Chats;
using AI.Client.Domain.Projects;

namespace AI.Client.Infrastructure.Storage;

public interface IChatStoragePaths
{
    string GetChatsDirectory(ProjectId projectId);
    string GetChatPath(ChatId chatId, ProjectId projectId);
    string GetTemporaryChatPath(ChatId chatId, ProjectId projectId);
}
