namespace AI.Client.Domain.Chats;

using Common;
using Projects;

public sealed class ChatThread
{
    private readonly Dictionary<ChatMessageId, ChatMessage> _messages = [];
    private readonly Dictionary<Guid, ChatBranch> _branches = [];

    public ChatThread(
        ChatId id,
        ProjectId projectId,
        string title,
        DateTimeOffset createdAt,
        ConnectionId? connectionId = null)
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
        ConnectionId = connectionId;
        _branches[id.Value] = new ChatBranch(id.Value, null, title.Trim());
    }

    public ChatId Id { get; }
    public ProjectId ProjectId { get; }
    public string Title { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyCollection<ChatMessage> Messages => _messages.Values;
    public ConnectionId? ConnectionId { get; private set; }
    public IReadOnlyCollection<ChatBranch> Branches => _branches.Values;

    public void RestoreBranches(IEnumerable<ChatBranch> branches)
    {
        var restored = branches.ToDictionary(branch => branch.Id);
        if (!restored.ContainsKey(Id.Value)
            || restored.Values.Any(branch => branch.HeadMessageId is { } head && !_messages.ContainsKey(head)))
            throw new DomainException("Invalid chat branches.");
        _branches.Clear();
        foreach (var branch in restored.Values) _branches.Add(branch.Id, branch);
    }

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

    public void SetConnection(ConnectionId? connectionId, DateTimeOffset updatedAt)
    {
        EnsureTimestampDoesNotMoveBackwards(updatedAt);

        ConnectionId = connectionId;
        UpdatedAt = updatedAt;
    }

    public void AddMessage(ChatMessage message, DateTimeOffset updatedAt, Guid? branchId = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.ParentId is { } parentId && !_messages.ContainsKey(parentId))
        {
            throw new DomainException("A message parent must exist in the same chat.");
        }

        EnsureTimestampDoesNotMoveBackwards(updatedAt);
        if (!_messages.TryAdd(message.Id, message))
        {
            throw new DomainException($"Chat message '{message.Id}' already exists.");
        }

        var branch = branchId is { } requested ? _branches.GetValueOrDefault(requested)
            : _branches.Values.FirstOrDefault(item => item.HeadMessageId == message.ParentId);
        if (branch is null)
        {
            var parentBranch = _branches.Values.FirstOrDefault(item => GetBranch(item.HeadMessageId).Any(parent => parent.Id == message.ParentId));
            var id = branchId ?? message.Id.Value;
            branch = new ChatBranch(id, message.ParentId, message.Content[..Math.Min(48, message.Content.Length)], parentBranch?.Id ?? Id.Value, message.Id);
        }
        _branches[branch.Id] = branch with { HeadMessageId = message.Id };
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
        if (!_branches.ContainsKey(rootId.Value)) throw new DomainException("Branch does not exist in this chat.");
        if (string.IsNullOrWhiteSpace(title)) throw new DomainException("Branch title cannot be empty.");
        EnsureTimestampDoesNotMoveBackwards(updatedAt);
        if (_branches.TryGetValue(rootId.Value, out var branch)) _branches[rootId.Value] = branch with { Title = title.Trim() };
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
        foreach (var id in removed) { _messages.Remove(id); }
        foreach (var branch in _branches.Values.ToArray())
        {
            if (branch.RootMessageId is { } branchRoot && removed.Contains(branchRoot)) _branches.Remove(branch.Id);
            else if (branch.HeadMessageId is { } head && removed.Contains(head)) _branches[branch.Id] = branch with { HeadMessageId = root.ParentId };
        }
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
