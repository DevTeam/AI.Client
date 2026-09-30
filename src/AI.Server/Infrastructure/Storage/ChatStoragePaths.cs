namespace AI.Infrastructure.Storage;

using Domain.Chats;
using AI.Domain.Projects;

public sealed class ChatStoragePaths(IProjectStorageLocation location) : IChatStoragePaths
{
    public string GetChatsDirectory(ProjectId projectId) => Path.Combine(location.RootDirectory, "projects", projectId.Value.ToString("N"), "chats");
    public string GetChatPath(ChatId chatId, ProjectId projectId) => Path.Combine(GetChatsDirectory(projectId), $"{chatId.Value:N}.json");
    public string GetTemporaryChatPath(ChatId chatId, ProjectId projectId) => Path.Combine(GetChatsDirectory(projectId), $"{chatId.Value:N}.json.tmp");
    public string GetChatSummaryPath(ChatId chatId, ProjectId projectId) => Path.Combine(GetChatsDirectory(projectId), $"{chatId.Value:N}.summary.json");
    public string GetTemporaryChatSummaryPath(ChatId chatId, ProjectId projectId) => Path.Combine(GetChatsDirectory(projectId), $"{chatId.Value:N}.summary.json.tmp");
    public string GetHistoryCheckpointsPath(ChatId chatId, ProjectId projectId) => Path.Combine(GetChatsDirectory(projectId), $"{chatId.Value:N}.context.json");
}
