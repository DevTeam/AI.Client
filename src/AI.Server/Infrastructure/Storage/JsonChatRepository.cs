namespace AI.Infrastructure.Storage;

using Application.Chats;
using Contracts.Chats;
using Domain.Chats;
using AI.Domain.Projects;
using System.Text.Json.Nodes;

public sealed class JsonChatRepository(
    ITextFileSystem fileSystem,
    IChatStoragePaths paths,
    IChatDocumentSerializer serializer) : IChatRepository, IDisposable
{
    public void Dispose() => _writes.Dispose();

    private readonly AsyncGate _writes = new();

    public async Task<IReadOnlyList<StoredChatSummary>> ListSummariesAsync(ProjectId projectId, CancellationToken cancellationToken)
    {
        var files = await fileSystem.ListFilesAsync(paths.GetChatsDirectory(projectId), "*.summary.json", cancellationToken);
        var chats = new List<StoredChatSummary>();
        foreach (var file in files)
        {
            var json = await fileSystem.ReadTextAsync(file, cancellationToken);
            if (json is not null)
            {
                var summary = serializer.DeserializeSummary(json);
                chats.Add(summary.HasStoredBranchCount
                    ? summary
                    : await MigrateBranchCountAsync(summary, cancellationToken));
            }
        }

        return chats;
    }

    private async Task<StoredChatSummary> MigrateBranchCountAsync(
        StoredChatSummary staleSummary,
        CancellationToken cancellationToken)
    {
        using var lease = await _writes.EnterAsync(cancellationToken);
        var summaryPath = paths.GetChatSummaryPath(staleSummary.Id, staleSummary.ProjectId);

        // A save may have completed between the list read and acquiring the write gate. Re-read
        // the manifest and avoid touching the full transcript when it has already been upgraded.
        var currentSummaryJson = await fileSystem.ReadTextAsync(summaryPath, cancellationToken);
        if (currentSummaryJson is null) return staleSummary;
        var currentSummary = serializer.DeserializeSummary(currentSummaryJson);
        if (currentSummary.HasStoredBranchCount) return currentSummary;

        var chatJson = await fileSystem.ReadTextAsync(
            paths.GetChatPath(currentSummary.Id, currentSummary.ProjectId), cancellationToken);
        if (chatJson is null) return currentSummary;

        var stored = serializer.Deserialize(chatJson);
        var upgradedJson = serializer.SerializeSummary(stored.Chat, stored.Revision);
        var temporarySummaryPath = paths.GetTemporaryChatSummaryPath(currentSummary.Id, currentSummary.ProjectId);
        await fileSystem.WriteTextAsync(temporarySummaryPath, upgradedJson, cancellationToken);
        await fileSystem.MoveAsync(temporarySummaryPath, summaryPath, true, cancellationToken);
        return serializer.DeserializeSummary(upgradedJson);
    }

    public async Task<StoredChat?> GetAsync(ProjectId projectId, ChatId id, CancellationToken cancellationToken)
    {
        var json = await fileSystem.ReadTextAsync(paths.GetChatPath(id, projectId), cancellationToken);
        return json is null ? null : serializer.Deserialize(json);
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
        var summaryPath = paths.GetChatSummaryPath(chat.Id, chat.ProjectId);
        var temporarySummaryPath = paths.GetTemporaryChatSummaryPath(chat.Id, chat.ProjectId);
        await fileSystem.WriteTextAsync(temporaryPath, serializer.Serialize(chat, nextRevision), cancellationToken);
        await fileSystem.WriteTextAsync(temporarySummaryPath, serializer.SerializeSummary(chat, nextRevision), cancellationToken);
        await fileSystem.MoveAsync(temporaryPath, path, true, cancellationToken);
        await fileSystem.MoveAsync(temporarySummaryPath, summaryPath, true, cancellationToken);
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
        await fileSystem.DeleteAsync(paths.GetChatSummaryPath(id, projectId), cancellationToken);
        return new ChatDeleteResult(true, revision);
    }
}
