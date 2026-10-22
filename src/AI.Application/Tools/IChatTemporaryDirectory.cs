namespace AI.Application.Tools;

/// <summary>Manages the private operating-system temporary directory shared by a chat's branches.</summary>
public interface IChatTemporaryDirectory
{
    string GetOrCreate(Guid projectId, Guid chatId);

    /// <summary>The directory holding every chat directory of the project; it is not created.</summary>
    string ProjectRoot(Guid projectId);

    void DeleteChat(Guid projectId, Guid chatId);

    void DeleteProject(Guid projectId);
}
