using AI.Client.Domain.Common;
using AI.Client.Domain.Projects;

namespace AI.Client.Domain.Chats;

public sealed class ChatThread
{
    private readonly Dictionary<ChatMessageId, ChatMessage> _messages = [];
    private readonly Dictionary<ChatMessageId, string> _branchTitles = [];

    public ChatThread(
        ChatId id,
        ProjectId projectId,
        string title,
        DateTimeOffset createdAt,
        EndpointProfileId? endpointProfileId = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Chat title cannot be empty.");
        }

        Id = id;
        ProjectId = projectId;
        Title = title.Trim();
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        EndpointProfileId = endpointProfileId;
    }

    public ChatId Id { get; }
    public ProjectId ProjectId { get; }
    public string Title { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyCollection<ChatMessage> Messages => _messages.Values;
    public EndpointProfileId? EndpointProfileId { get; private set; }
    public IReadOnlyDictionary<ChatMessageId, string> BranchTitles => _branchTitles;

    public void Rename(string title, DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Chat title cannot be empty.");
        }

        EnsureTimestampDoesNotMoveBackwards(updatedAt);
        Title = title.Trim();
        UpdatedAt = updatedAt;
    }

    public void SetEndpointProfile(EndpointProfileId? endpointProfileId, DateTimeOffset updatedAt)
    {
        EnsureTimestampDoesNotMoveBackwards(updatedAt);

        EndpointProfileId = endpointProfileId;
        UpdatedAt = updatedAt;
    }

    public void AddMessage(ChatMessage message, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.ParentId is { } parentId && !_messages.ContainsKey(parentId))
        {
            throw new DomainException("A message parent must exist in the same chat.");
        }

        if (!_messages.TryAdd(message.Id, message))
        {
            throw new DomainException($"Chat message '{message.Id}' already exists.");
        }

        EnsureTimestampDoesNotMoveBackwards(updatedAt);

        UpdatedAt = updatedAt;
    }

    public IReadOnlyList<ChatMessage> GetBranch(ChatMessageId? leafId)
    {
        if (leafId is null)
        {
            return [];
        }

        if (!_messages.TryGetValue(leafId.Value, out var message))
        {
            throw new DomainException("Branch message does not exist in this chat.");
        }

        var branch = new List<ChatMessage>();
        while (true)
        {
            branch.Add(message);
            if (message.ParentId is not { } parentId)
            {
                break;
            }

            message = _messages[parentId];
        }

        branch.Reverse();
        return branch;
    }

    public void RenameBranch(ChatMessageId rootId, string title, DateTimeOffset updatedAt)
    {
        if (!_messages.ContainsKey(rootId)) throw new DomainException("Branch root does not exist in this chat.");
        if (string.IsNullOrWhiteSpace(title)) throw new DomainException("Branch title cannot be empty.");
        EnsureTimestampDoesNotMoveBackwards(updatedAt);
        _branchTitles[rootId] = title.Trim();
        UpdatedAt = updatedAt;
    }

    public ChatMessageId? DeleteBranch(ChatMessageId rootId, DateTimeOffset updatedAt)
    {
        if (!_messages.TryGetValue(rootId, out var root)) throw new DomainException("Branch root does not exist in this chat.");
        EnsureTimestampDoesNotMoveBackwards(updatedAt);
        var removed = new HashSet<ChatMessageId> { rootId };
        while (true)
        {
            var children = _messages.Values.Where(message => message.ParentId is { } parentId && removed.Contains(parentId)).Select(message => message.Id).Where(removed.Add).ToArray();
            if (children.Length == 0) break;
        }
        foreach (var id in removed) { _messages.Remove(id); _branchTitles.Remove(id); }
        UpdatedAt = updatedAt;
        return root.ParentId;
    }

    private void EnsureTimestampDoesNotMoveBackwards(DateTimeOffset updatedAt)
    {
        if (updatedAt < UpdatedAt)
        {
            throw new DomainException("Chat update timestamp cannot move backwards.");
        }
    }
}
