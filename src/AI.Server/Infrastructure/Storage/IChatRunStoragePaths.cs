namespace AI.Infrastructure.Storage;

public interface IChatRunStoragePaths
{
    string GetPath(Guid projectId, Guid chatId, Guid branchId);
    string GetTemporaryPath(Guid projectId, Guid chatId, Guid branchId);
    string GetChatsDirectory(Guid projectId);
    string RootDirectory { get; }
}
