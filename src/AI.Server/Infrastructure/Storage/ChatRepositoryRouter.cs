namespace AI.Infrastructure.Storage;

using System.Collections.Concurrent;
using AI.Application.Chats;
using AI.Domain.Chats;
using AI.Domain.Projects;

/// <summary>Routes a chat by its policy while keeping lookup independent of any known kind.</summary>
public sealed class ChatRepositoryRouter(IPersistentChatRepository durable, IHostLifetimeChatRepository hostLifetime,
    IChatKindPolicyRegistry kinds) : IChatRepository
{
    public async Task<IReadOnlyList<StoredChatSummary>> ListSummariesAsync(ProjectId projectId, CancellationToken cancellationToken) =>
        [.. await durable.ListSummariesAsync(projectId, cancellationToken), .. await hostLifetime.ListSummariesAsync(projectId, cancellationToken)];

    public async Task<StoredChat?> GetAsync(ProjectId projectId, ChatId id, CancellationToken cancellationToken) =>
        await hostLifetime.GetAsync(projectId, id, cancellationToken) ?? await durable.GetAsync(projectId, id, cancellationToken);

    public Task<ChatSaveResult> SaveAsync(ChatThread chat, long expectedRevision, CancellationToken cancellationToken) =>
        Backend(chat.Kind).SaveAsync(chat, expectedRevision, cancellationToken);

    public async Task<AI.Contracts.Chats.ChatDeleteResult> DeleteAsync(ProjectId projectId, ChatId id,
        long expectedRevision, CancellationToken cancellationToken)
    {
        IChatRepository backend = await hostLifetime.GetAsync(projectId, id, cancellationToken) is null ? durable : hostLifetime;
        return await backend.DeleteAsync(projectId, id, expectedRevision, cancellationToken);
    }

    private IChatRepository Backend(ChatKind kind) =>
        kinds.Resolve(kind).Behavior.Persistence == ChatPersistence.HostLifetime ? hostLifetime : durable;
}

/// <summary>Versioned chat documents kept only for the lifetime of the host.</summary>
public sealed class HostLifetimeChatRepository(IChatDocumentSerializer serializer) : IHostLifetimeChatRepository
{
    private readonly ConcurrentDictionary<(ProjectId ProjectId, ChatId ChatId), Entry> _documents = new();

    public Task<IReadOnlyList<StoredChatSummary>> ListSummariesAsync(ProjectId projectId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<StoredChatSummary> summaries = _documents
            .Where(item => item.Key.ProjectId == projectId)
            .Select(item => serializer.DeserializeSummary(item.Value.Summary)).ToArray();
        return Task.FromResult(summaries);
    }

    public Task<StoredChat?> GetAsync(ProjectId projectId, ChatId id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_documents.TryGetValue((projectId, id), out var entry)
            ? serializer.Deserialize(entry.Document) : null);
    }

    public Task<ChatSaveResult> SaveAsync(ChatThread chat, long expectedRevision, CancellationToken cancellationToken)
    {
        var key = (chat.ProjectId, chat.Id);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _documents.TryGetValue(key, out var current);
            var revision = current?.Revision ?? 0;
            if (revision != expectedRevision) return Task.FromResult(ChatSaveResult.Conflict(revision));
            var next = checked(revision + 1);
            var replacement = new Entry(serializer.Serialize(chat, next), serializer.SerializeSummary(chat, next), next);
            if (current is null ? _documents.TryAdd(key, replacement)
                    : _documents.TryUpdate(key, replacement, current))
                return Task.FromResult(ChatSaveResult.Saved(next));
        }
    }

    public Task<AI.Contracts.Chats.ChatDeleteResult> DeleteAsync(ProjectId projectId, ChatId id,
        long expectedRevision, CancellationToken cancellationToken)
    {
        var key = (projectId, id);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_documents.TryGetValue(key, out var current))
                return Task.FromResult(new AI.Contracts.Chats.ChatDeleteResult(false, 0));
            if (current.Revision != expectedRevision)
                return Task.FromResult(new AI.Contracts.Chats.ChatDeleteResult(false, current.Revision));
            if (((ICollection<KeyValuePair<(ProjectId ProjectId, ChatId ChatId), Entry>>)_documents)
                .Remove(new KeyValuePair<(ProjectId ProjectId, ChatId ChatId), Entry>(key, current)))
                return Task.FromResult(new AI.Contracts.Chats.ChatDeleteResult(true, expectedRevision));
        }
    }

    private sealed record Entry(string Document, string Summary, long Revision);
}
