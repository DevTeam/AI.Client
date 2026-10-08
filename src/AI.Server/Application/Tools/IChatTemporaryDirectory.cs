namespace AI.Application.Tools;

/// <summary>Manages the private operating-system temporary directory shared by a chat's branches.</summary>
public interface IChatTemporaryDirectory
{
    string GetOrCreate(Guid projectId, Guid chatId);

    void DeleteChat(Guid projectId, Guid chatId);

    void DeleteProject(Guid projectId);
}
