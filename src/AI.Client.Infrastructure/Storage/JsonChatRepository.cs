using AI.Client.Application.Chats;
using AI.Client.Contracts.Chats;
using AI.Client.Domain.Chats;
using AI.Client.Domain.Projects;

namespace AI.Client.Infrastructure.Storage;

public sealed class JsonChatRepository(
    ITextFileSystem fileSystem,
    IChatStoragePaths paths,
    IChatDocumentSerializer serializer) : IChatRepository
{
    public async Task<IReadOnlyList<StoredChat>> ListAsync(ProjectId projectId, CancellationToken cancellationToken)
    {
        var files = await fileSystem.ListFilesAsync(paths.GetChatsDirectory(projectId), "*.json", cancellationToken);
        var chats = new List<StoredChat>();
        foreach (var file in files.Where(file => !file.EndsWith(".run.json", StringComparison.OrdinalIgnoreCase)))
        {
            var json = await fileSystem.ReadTextAsync(file, cancellationToken);
            if (json is not null)
            {
                chats.Add(serializer.Deserialize(json));
            }
        }

        return chats;
    }

    public async Task<StoredChat?> GetAsync(ProjectId projectId, ChatId id, CancellationToken cancellationToken)
    {
        var json = await fileSystem.ReadTextAsync(paths.GetChatPath(id, projectId), cancellationToken);
        return json is null ? null : serializer.Deserialize(json);
    }

    public async Task<ChatSaveResult> SaveAsync(ChatThread chat, long expectedRevision, CancellationToken cancellationToken)
    {
        var path = paths.GetChatPath(chat.Id, chat.ProjectId);
        var current = await fileSystem.ReadTextAsync(path, cancellationToken);
        var revision = current is null ? 0 : serializer.Deserialize(current).Revision;
        if (revision != expectedRevision)
        {
            return ChatSaveResult.Conflict(revision);
        }

        var nextRevision = checked(revision + 1);
        var temporaryPath = paths.GetTemporaryChatPath(chat.Id, chat.ProjectId);
        await fileSystem.WriteTextAsync(temporaryPath, serializer.Serialize(chat, nextRevision), cancellationToken);
        await fileSystem.MoveAsync(temporaryPath, path, true, cancellationToken);
        return ChatSaveResult.Saved(nextRevision);
    }

    public async Task<ChatDeleteResult> DeleteAsync(
        ProjectId projectId,
        ChatId id,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        var path = paths.GetChatPath(id, projectId);
        var current = await fileSystem.ReadTextAsync(path, cancellationToken);
        if (current is null) return new ChatDeleteResult(false, 0);
        var revision = serializer.Deserialize(current).Revision;
        if (revision != expectedRevision) return new ChatDeleteResult(false, revision);
        await fileSystem.DeleteAsync(path, cancellationToken);
        return new ChatDeleteResult(true, revision);
    }
}
