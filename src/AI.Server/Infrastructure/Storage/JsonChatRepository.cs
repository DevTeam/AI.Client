namespace AI.Infrastructure.Storage;

using Application.Chats;
using AI.Contracts.FileSystem;
using Contracts.Chats;
using Domain.Chats;
using AI.Domain.Projects;
using System.Text.Json.Nodes;

public sealed class JsonChatRepository(
    IFileSystem fileSystem,
    IChatStoragePaths paths,
    IChatDocumentSerializer serializer) : IPersistentChatRepository, IDisposable
{
    public void Dispose() => _writes.Dispose();

    private readonly AsyncGate _writes = new();
    private const long MaxReviewLookupDocumentBytes = 48L * 1024 * 1024;
    private const long MaxReviewLookupCacheBytes = 96L * 1024 * 1024;
    private readonly Lock _reviewLookupsGate = new();
    private readonly Dictionary<string, ReviewLookup> _reviewLookups = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly LinkedList<string> _reviewLookupOrder = new();
    private long _reviewLookupBytes;

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
                chats.Add(summary.HasCurrentManifest
                    ? summary
                    : await MigrateSummaryAsync(summary, cancellationToken));
            }
        }

        return chats;
    }

    private async Task<StoredChatSummary> MigrateSummaryAsync(
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
        if (currentSummary.HasCurrentManifest) return currentSummary;

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
        var path = paths.GetChatPath(id, projectId);
        var before = await GetStampAsync(path, cancellationToken);
        var json = await fileSystem.ReadTextAsync(path, cancellationToken);
        if (json is null) return null;
        var stored = serializer.Deserialize(json);
        if (before is { Length: <= MaxReviewLookupDocumentBytes } stamp
            && await GetStampAsync(path, cancellationToken) == stamp)
        {
            var messages = stored.Chat.Messages.ToDictionary(message => message.Id.Value);
            var reviewIds = messages.Values
                .Where(message => message.Role == ChatMessageRole.User)
                .SelectMany(message => message.Resources ?? [])
                .Where(resource => resource.Kind == AI.Domain.Resources.ChatResourceKind.Review)
                .Select(resource => resource.Id).ToHashSet();
            StoreReviewLookup(new ReviewLookup(path, stamp, messages, reviewIds,
                new LinkedListNode<string>(path)));
        }
        return stored;
    }

    public async Task<ChatMessageLookup?> GetMessageAsync(ProjectId projectId, ChatId id,
        ChatMessageId messageId, CancellationToken cancellationToken)
    {
        var path = paths.GetChatPath(id, projectId);
        if (await CurrentReviewLookupAsync(path, cancellationToken) is { } lookup)
            return new ChatMessageLookup(lookup.Messages.GetValueOrDefault(messageId.Value));
        var json = await fileSystem.ReadTextAsync(path, cancellationToken);
        return json is null ? null : new ChatMessageLookup(serializer.DeserializeMessage(json, messageId.Value));
    }

    public async Task<bool?> MayContainReviewReferenceAsync(ProjectId projectId, ChatId id,
        Guid reviewId, CancellationToken cancellationToken)
    {
        var path = paths.GetChatPath(id, projectId);
        if (await CurrentReviewLookupAsync(path, cancellationToken) is { } lookup)
            return lookup.ReviewIds.Contains(reviewId);
        var json = await fileSystem.ReadTextAsync(path, cancellationToken);
        // A negative check is definitive for GUIDs written by System.Text.Json. A positive check
        // falls back to the domain model because the ID may occur in unrelated message content.
        return json?.Contains(reviewId.ToString("D"), StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ReviewLookup?> CurrentReviewLookupAsync(string path, CancellationToken cancellationToken)
    {
        ReviewLookup? lookup;
        lock (_reviewLookupsGate) _reviewLookups.TryGetValue(path, out lookup);
        if (lookup is null) return null;
        if (await GetStampAsync(path, cancellationToken) != lookup.Stamp)
        {
            lock (_reviewLookupsGate)
                if (_reviewLookups.TryGetValue(path, out var current) && ReferenceEquals(current, lookup))
                    RemoveReviewLookup(path);
            return null;
        }
        lock (_reviewLookupsGate)
        {
            if (_reviewLookups.TryGetValue(path, out var current) && ReferenceEquals(current, lookup))
            {
                _reviewLookupOrder.Remove(lookup.Node);
                _reviewLookupOrder.AddFirst(lookup.Node);
            }
        }
        return lookup;
    }

    private void StoreReviewLookup(ReviewLookup lookup)
    {
        lock (_reviewLookupsGate)
        {
            RemoveReviewLookup(lookup.Path);
            _reviewLookups.Add(lookup.Path, lookup);
            _reviewLookupOrder.AddFirst(lookup.Node);
            _reviewLookupBytes += lookup.Stamp.Length;
            while (_reviewLookupBytes > MaxReviewLookupCacheBytes)
                RemoveReviewLookup(_reviewLookupOrder.Last!.Value);
        }
    }

    private void InvalidateReviewLookup(string path)
    {
        lock (_reviewLookupsGate) RemoveReviewLookup(path);
    }

    // Called only while holding _reviewLookupsGate.
    private void RemoveReviewLookup(string path)
    {
        if (!_reviewLookups.Remove(path, out var lookup)) return;
        _reviewLookupOrder.Remove(lookup.Node);
        _reviewLookupBytes -= lookup.Stamp.Length;
    }

    private async Task<FileStamp?> GetStampAsync(string path, CancellationToken cancellationToken)
    {
        var entry = await fileSystem.GetEntryAsync(path, cancellationToken);
        return entry is { IsDirectory: false }
            ? new FileStamp(entry.Length, await fileSystem.GetLastWriteTimeAsync(path, cancellationToken)) : null;
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
        InvalidateReviewLookup(path);
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
        await fileSystem.DeleteFileAsync(path, cancellationToken);
        InvalidateReviewLookup(path);
        await fileSystem.DeleteFileAsync(paths.GetChatSummaryPath(id, projectId), cancellationToken);
        await fileSystem.DeleteFileAsync(paths.GetHistoryCheckpointsPath(id, projectId), cancellationToken);
        return new ChatDeleteResult(true, revision);
    }

    private sealed record FileStamp(long Length, DateTimeOffset LastWriteTime);
    private sealed record ReviewLookup(string Path, FileStamp Stamp,
        IReadOnlyDictionary<Guid, ChatMessage> Messages, IReadOnlySet<Guid> ReviewIds,
        LinkedListNode<string> Node);
}
