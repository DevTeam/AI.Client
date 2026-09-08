namespace AI.Client.Infrastructure.Storage;

using Application.Chats;
using Contracts.Chats;
using Domain.Chats;
using AI.Client.Domain.Projects;
using System.Text.Json.Nodes;

public sealed class JsonChatRepository(
    ITextFileSystem fileSystem,
    ChatStoragePaths paths) : IChatRepository, IDisposable
{
    public void Dispose() => _writes.Dispose();

    private readonly AsyncGate _writes = new();

    private async Task<StoredChat> ReadAsync(string path, string json, CancellationToken cancellationToken)
    {
        var document = JsonNode.Parse(json)!.AsObject();
        var ids = document["MessageIds"]?.AsArray() ?? throw new InvalidOperationException("Unsupported chat storage format.");
        {
            var messages = new JsonArray();
            foreach (var id in ids)
            {
                var node = await fileSystem.ReadTextAsync(NodePath(path, id!.GetValue<Guid>()), cancellationToken)
                    ?? throw new InvalidOperationException("A referenced chat message is missing.");
                messages.Add(JsonNode.Parse(node));
            }
            document["Messages"] = messages;
        }
        return ChatDocumentSerializer.Deserialize(document.ToJsonString());
    }

    private static string NodePath(string chatPath, Guid id) => Path.Combine(chatPath + ".nodes", $"{id:N}.json");
    public async Task<IReadOnlyList<StoredChat>> ListAsync(ProjectId projectId, CancellationToken cancellationToken)
    {
        var files = await fileSystem.ListFilesAsync(paths.GetChatsDirectory(projectId), "*.json", cancellationToken);
        var chats = new List<StoredChat>();
        foreach (var file in files.Where(file => !file.EndsWith(".run.json", StringComparison.OrdinalIgnoreCase)))
        {
            var json = await fileSystem.ReadTextAsync(file, cancellationToken);
            if (json is not null)
            {
                chats.Add(await ReadAsync(file, json, cancellationToken));
            }
        }

        return chats;
    }

    public async Task<StoredChat?> GetAsync(ProjectId projectId, ChatId id, CancellationToken cancellationToken)
    {
        var json = await fileSystem.ReadTextAsync(paths.GetChatPath(id, projectId), cancellationToken);
        return json is null ? null : await ReadAsync(paths.GetChatPath(id, projectId), json, cancellationToken);
    }

    public async Task<ChatSaveResult> SaveAsync(ChatThread chat, long expectedRevision, CancellationToken cancellationToken)
    {
        using var lease = await _writes.EnterAsync(cancellationToken);
        var path = paths.GetChatPath(chat.Id, chat.ProjectId);
        var current = await fileSystem.ReadTextAsync(path, cancellationToken);
        var revision = current is null ? 0 : JsonNode.Parse(current)!["Revision"]!.GetValue<long>();
        if (revision != expectedRevision)
        {
            return ChatSaveResult.Conflict(revision);
        }

        var nextRevision = checked(revision + 1);
        var temporaryPath = paths.GetTemporaryChatPath(chat.Id, chat.ProjectId);
        var document = JsonNode.Parse(ChatDocumentSerializer.Serialize(chat, nextRevision))!.AsObject();
        var messages = document["Messages"]!.AsArray();
        var ids = new JsonArray();
        foreach (var message in messages)
        {
            var id = message!["Id"]!.GetValue<Guid>();
            ids.Add(JsonValue.Create(id));
            var nodePath = NodePath(path, id);
            var existing = await fileSystem.ReadTextAsync(nodePath, cancellationToken);
            if (existing is null)
            {
                await fileSystem.WriteTextAsync(nodePath + ".tmp", message.ToJsonString(), cancellationToken);
                await fileSystem.MoveAsync(nodePath + ".tmp", nodePath, false, cancellationToken);
            }
            else if (!JsonNode.DeepEquals(JsonNode.Parse(existing), message))
                throw new InvalidOperationException("Immutable message content cannot change.");
        }
        document.Remove("Messages");
        document["MessageIds"] = ids;
        await fileSystem.WriteTextAsync(temporaryPath, document.ToJsonString(), cancellationToken);
        await fileSystem.MoveAsync(temporaryPath, path, true, cancellationToken);
        var retained = chat.Messages.Select(message => message.Id.Value).ToHashSet();
        foreach (var nodePath in await fileSystem.ListFilesAsync(path + ".nodes", "*.json", cancellationToken))
            if (Guid.TryParse(Path.GetFileNameWithoutExtension(nodePath), out var id) && !retained.Contains(id))
                await fileSystem.DeleteAsync(nodePath, cancellationToken);
        return ChatSaveResult.Saved(nextRevision);
    }

    public async Task<ChatDeleteResult> DeleteAsync(
        ProjectId projectId,
        ChatId id,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        using var lease = await _writes.EnterAsync(cancellationToken);
        var path = paths.GetChatPath(id, projectId);
        var current = await fileSystem.ReadTextAsync(path, cancellationToken);
        if (current is null) return new ChatDeleteResult(false, 0);
        var revision = JsonNode.Parse(current)!["Revision"]!.GetValue<long>();
        if (revision != expectedRevision) return new ChatDeleteResult(false, revision);
        await fileSystem.DeleteAsync(path, cancellationToken);
        return new ChatDeleteResult(true, revision);
    }
}
