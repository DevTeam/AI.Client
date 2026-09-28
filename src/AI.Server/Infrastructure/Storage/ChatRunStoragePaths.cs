namespace AI.Infrastructure.Storage;

public sealed class ChatRunStoragePaths(IProjectStorageLocation location) : IChatRunStoragePaths
{
    public string GetPath(Guid projectId, Guid chatId, Guid branchId) => Path.Combine(location.RootDirectory, "projects", projectId.ToString("N"), "chats", $"{chatId:N}.{branchId:N}.run.json");
    public string GetTemporaryPath(Guid projectId, Guid chatId, Guid branchId) => GetPath(projectId, chatId, branchId) + ".tmp";
    public string GetChatsDirectory(Guid projectId) => Path.Combine(location.RootDirectory, "projects", projectId.ToString("N"), "chats");
    public string RootDirectory => Path.Combine(location.RootDirectory, "projects");
}
