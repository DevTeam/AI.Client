namespace AI.Client.Infrastructure.Storage;

using Domain.Chats;
using AI.Client.Domain.Projects;

public interface IChatStoragePaths
{
    string GetChatsDirectory(ProjectId projectId);
    string GetChatPath(ChatId chatId, ProjectId projectId);
    string GetTemporaryChatPath(ChatId chatId, ProjectId projectId);
    string GetChatSummaryPath(ChatId chatId, ProjectId projectId);
    string GetTemporaryChatSummaryPath(ChatId chatId, ProjectId projectId);
}
