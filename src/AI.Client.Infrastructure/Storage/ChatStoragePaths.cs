using AI.Client.Domain.Chats;
using AI.Client.Domain.Projects;

namespace AI.Client.Infrastructure.Storage;

public sealed class ChatStoragePaths(string rootDirectory) : IChatStoragePaths
{
    public string GetChatsDirectory(ProjectId projectId) => Path.Combine(rootDirectory, "projects", projectId.Value.ToString("N"), "chats");
    public string GetChatPath(ChatId chatId, ProjectId projectId) => Path.Combine(GetChatsDirectory(projectId), $"{chatId.Value:N}.json");
    public string GetTemporaryChatPath(ChatId chatId, ProjectId projectId) => Path.Combine(GetChatsDirectory(projectId), $"{chatId.Value:N}.json.tmp");
}
