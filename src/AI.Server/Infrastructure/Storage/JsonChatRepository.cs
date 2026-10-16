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
    // Parsed chats by path, least recently used last, bounded by the size of their documents.
    private const long MaxCachedDocumentBytes = 48L * 1024 * 1024;
    private const long MaxCacheBytes = 96L * 1024 * 1024;
    private readonly Lock _documentsGate = new();
    private readonly Dictionary<string, CachedDocument> _documents = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly LinkedList<string> _documentOrder = new();
    private long _documentBytes;

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
        if (await CurrentDocumentAsync(path, cancellationToken) is { } cached)
            return new StoredChat(cached.Chat.Clone(), cached.Revision);
        var before = await GetStampAsync(path, cancellationToken);
        var json = await fileSystem.ReadTextAsync(path, cancellationToken);
        if (json is null) return null;
        var stored = serializer.Deserialize(json);
        // Cached only when nothing wrote the file while it was read, so the stamp names this content.
        if (before is { } stamp && await GetStampAsync(path, cancellationToken) == stamp)
        {
            StoreDocument(path, stamp, stored.Chat, stored.Revision);
            return stored with { Chat = stored.Chat.Clone() };
        }
        return stored;
    }

    public async Task<ChatMessageLookup?> GetMessageAsync(ProjectId projectId, ChatId id,
        ChatMessageId messageId, CancellationToken cancellationToken)
    {
        var path = paths.GetChatPath(id, projectId);
        if (await CurrentDocumentAsync(path, cancellationToken) is { } cached)
            return new ChatMessageLookup(cached.Messages.GetValueOrDefault(messageId.Value));
        var json = await fileSystem.ReadTextAsync(path, cancellationToken);
        return json is null ? null : new ChatMessageLookup(serializer.DeserializeMessage(json, messageId.Value));
    }

    public async Task<bool?> MayContainReviewReferenceAsync(ProjectId projectId, ChatId id,
        Guid reviewId, CancellationToken cancellationToken)
    {
        var path = paths.GetChatPath(id, projectId);
        if (await CurrentDocumentAsync(path, cancellationToken) is { } cached)
            return cached.ReviewIds.Contains(reviewId);
        var json = await fileSystem.ReadTextAsync(path, cancellationToken);
        // A negative check is definitive for GUIDs written by System.Text.Json. A positive check
        // falls back to the domain model because the ID may occur in unrelated message content.
        return json?.Contains(reviewId.ToString("D"), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The parsed chat whose stamp still matches the file, or null. A stamp is the file's length and
    /// last write time: every write this repository makes replaces the entry with what it wrote, and a
    /// write by anyone else changes the stamp, so a stale entry is never answered from.
    /// </summary>
    private async Task<CachedDocument?> CurrentDocumentAsync(string path, CancellationToken cancellationToken)
    {
        CachedDocument? cached;
        lock (_documentsGate) _documents.TryGetValue(path, out cached);
        if (cached is null) return null;
        if (await GetStampAsync(path, cancellationToken) != cached.Stamp)
        {
            lock (_documentsGate)
                if (_documents.TryGetValue(path, out var current) && ReferenceEquals(current, cached))
                    RemoveDocument(path);
            return null;
        }
        lock (_documentsGate)
        {
            if (_documents.TryGetValue(path, out var current) && ReferenceEquals(current, cached))
            {
                _documentOrder.Remove(cached.Node);
                _documentOrder.AddFirst(cached.Node);
            }
        }
        return cached;
    }

    /// <summary>
    /// Keeps <paramref name="chat"/> as the parsed form of the file with <paramref name="stamp"/>.
    /// The caller hands it over: nothing may change it afterwards, and readers get clones.
    /// </summary>
    private void StoreDocument(string path, FileStamp stamp, ChatThread chat, long revision)
    {
        lock (_documentsGate)
        {
            RemoveDocument(path);
            if (stamp.Length > MaxCachedDocumentBytes) return;
            var document = new CachedDocument(path, stamp, chat, revision, new LinkedListNode<string>(path));
            _documents.Add(path, document);
            _documentOrder.AddFirst(document.Node);
            _documentBytes += stamp.Length;
            while (_documentBytes > MaxCacheBytes)
                RemoveDocument(_documentOrder.Last!.Value);
        }
    }

    private void InvalidateDocument(string path)
    {
        lock (_documentsGate) RemoveDocument(path);
    }

    // Called only while holding _documentsGate.
    private void RemoveDocument(string path)
    {
        if (!_documents.Remove(path, out var document)) return;
        _documentOrder.Remove(document.Node);
        _documentBytes -= document.Stamp.Length;
    }

    private async Task<FileStamp?> GetStampAsync(string path, CancellationToken cancellationToken)
    {
        var entry = await fileSystem.GetEntryAsync(path, cancellationToken);
        return entry is { IsDirectory: false }
            ? new FileStamp(entry.Length, await fileSystem.GetLastWriteTimeAsync(path, cancellationToken)) : null;
    }

    /// <summary>
    /// Saves when the stored revision is still <paramref name="expectedRevision"/>. The revision is
    /// taken from the cached document while its stamp matches the file, so a save in the agent loop
    /// costs stamp reads instead of a read and parse of the whole transcript; the document is read
    /// only when something else wrote it. What was written becomes the cached document.
    /// </summary>
    public async Task<ChatSaveResult> SaveAsync(ChatThread chat, long expectedRevision, CancellationToken cancellationToken)
    {
        using var lease = await _writes.EnterAsync(cancellationToken);
        var path = paths.GetChatPath(chat.Id, chat.ProjectId);
        var revision = await CurrentDocumentAsync(path, cancellationToken) is { } cached
            ? cached.Revision
            : await ReadRevisionAsync(path, cancellationToken);
        if (revision != expectedRevision)
        {
            return ChatSaveResult.Conflict(revision);
        }

        var nextRevision = checked(revision + 1);
        var temporaryPath = paths.GetTemporaryChatPath(chat.Id, chat.ProjectId);
        var summaryPath = paths.GetChatSummaryPath(chat.Id, chat.ProjectId);
        var temporarySummaryPath = paths.GetTemporaryChatSummaryPath(chat.Id, chat.ProjectId);
        // The caller keeps its instance and may go on changing it, so the cache keeps its own copy.
        var saved = chat.Clone();
        InvalidateDocument(path);
        await fileSystem.WriteTextAsync(temporaryPath, serializer.Serialize(saved, nextRevision), cancellationToken);
        await fileSystem.WriteTextAsync(temporarySummaryPath, serializer.SerializeSummary(saved, nextRevision), cancellationToken);
        await fileSystem.MoveAsync(temporaryPath, path, true, cancellationToken);
        if (await GetStampAsync(path, cancellationToken) is { } stamp) StoreDocument(path, stamp, saved, nextRevision);
        await fileSystem.MoveAsync(temporarySummaryPath, summaryPath, true, cancellationToken);
        return ChatSaveResult.Saved(nextRevision);
    }

    private async Task<long> ReadRevisionAsync(string path, CancellationToken cancellationToken)
    {
        var current = await fileSystem.ReadTextAsync(path, cancellationToken);
        return current is null ? 0 : JsonNode.Parse(current)!["Revision"]!.GetValue<long>();
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
        InvalidateDocument(path);
        await fileSystem.DeleteFileAsync(path, cancellationToken);
        await fileSystem.DeleteFileAsync(paths.GetChatSummaryPath(id, projectId), cancellationToken);
        await fileSystem.DeleteFileAsync(paths.GetHistoryCheckpointsPath(id, projectId), cancellationToken);
        return new ChatDeleteResult(true, revision);
    }

    private sealed record FileStamp(long Length, DateTimeOffset LastWriteTime);

    private sealed class CachedDocument(string path, FileStamp stamp, ChatThread chat, long revision,
        LinkedListNode<string> node)
    {
        private IReadOnlyDictionary<Guid, ChatMessage>? _messages;
        private IReadOnlySet<Guid>? _reviewIds;

        public string Path { get; } = path;
        public FileStamp Stamp { get; } = stamp;
        /// <summary>Never changed and never handed out: readers get a clone.</summary>
        public ChatThread Chat { get; } = chat;
        public long Revision { get; } = revision;
        public LinkedListNode<string> Node { get; } = node;

        // Built on first use: most cached documents only ever serve whole-chat reads and saves.
        public IReadOnlyDictionary<Guid, ChatMessage> Messages =>
            _messages ??= Chat.Messages.ToDictionary(message => message.Id.Value);

        public IReadOnlySet<Guid> ReviewIds => _reviewIds ??= Chat.Messages
            .Where(message => message.Role == ChatMessageRole.User)
            .SelectMany(message => message.Resources ?? [])
            .Where(resource => resource.Kind == AI.Domain.Resources.ChatResourceKind.Review)
            .Select(resource => resource.Id).ToHashSet();
    }
}
