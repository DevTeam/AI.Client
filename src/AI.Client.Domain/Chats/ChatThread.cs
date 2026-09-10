namespace AI.Client.Domain.Chats;

using Common;
using Projects;

public sealed class ChatThread
{
    private readonly Dictionary<ChatMessageId, ChatMessage> _messages = [];
    private readonly Dictionary<Guid, ChatBranch> _branches = [];
    private readonly Dictionary<ToolIdentity, ToolPolicy> _toolPolicies = [];

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
        LastActivityAt = createdAt;
        ConnectionId = connectionId;
        _branches[id.Value] = new ChatBranch(id.Value, null, title.Trim());
    }

    public ChatId Id { get; }
    public ProjectId ProjectId { get; }
    public string Title { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset LastActivityAt { get; private set; }
    public bool IsPinned { get; private set; }
    public DateTimeOffset? PinnedAt { get; private set; }
    public IReadOnlyCollection<ChatMessage> Messages => _messages.Values;
    public ConnectionId? ConnectionId { get; private set; }
    public IReadOnlyCollection<ChatBranch> Branches => _branches.Values;
    public IReadOnlyCollection<ToolPolicy> ToolPolicies => _toolPolicies.Values;

    public void SetToolPolicy(ToolPolicy policy, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(policy);
        EnsureTimestampDoesNotMoveBackwards(updatedAt);
        foreach (var stale in _toolPolicies.Keys.Where(item => item.ServerId == policy.Tool.ServerId
                     && item.Name == policy.Tool.Name && item.SchemaHash != policy.Tool.SchemaHash).ToArray())
            _toolPolicies.Remove(stale);
        _toolPolicies[policy.Tool] = policy;
        UpdatedAt = updatedAt;
    }

    public void RemoveToolPolicy(ToolIdentity tool, DateTimeOffset updatedAt)
    {
        EnsureTimestampDoesNotMoveBackwards(updatedAt);
        _toolPolicies.Remove(tool);
        UpdatedAt = updatedAt;
    }

    public void RestoreBranches(IEnumerable<ChatBranch> branches)
    {
        var restored = branches.ToDictionary(branch => branch.Id);
        if (!restored.TryGetValue(Id.Value, out var mainBranch)) throw new DomainException("The main chat branch is missing.");
        if (mainBranch.ParentBranchId is not null || mainBranch.RootMessageId is not null)
            throw new DomainException("The main chat branch cannot have a parent or branch root.");
        foreach (var branch in restored.Values)
        {
            if (branch.Revision < 0) throw new DomainException($"Chat branch '{branch.Id}' has an invalid revision.");
            if (branch.HeadMessageId is { } head && !_messages.ContainsKey(head))
                throw new DomainException($"Chat branch '{branch.Id}' references a missing head '{head}'.");
            if (branch.RootMessageId is { } root && !_messages.ContainsKey(root))
                throw new DomainException($"Chat branch '{branch.Id}' references a missing root '{root}'.");
            if (branch.ParentBranchId is { } parent && !restored.ContainsKey(parent))
                throw new DomainException($"Chat branch '{branch.Id}' references a missing parent branch '{parent}'.");
            if (branch.ParentBranchId == branch.Id)
                throw new DomainException($"Chat branch '{branch.Id}' cannot be its own parent.");
            if (branch.RootMessageId is { } branchRoot && !GetBranch(branch.HeadMessageId).Any(message => message.Id == branchRoot))
                throw new DomainException($"Chat branch '{branch.Id}' root is not an ancestor of its head.");
        }
        foreach (var branch in restored.Values)
        {
            var visited = new HashSet<Guid> { branch.Id };
            var cursor = branch;
            while (cursor.ParentBranchId is { } parent)
            {
                if (!visited.Add(parent)) throw new DomainException("Chat branch hierarchy contains a cycle.");
                cursor = restored[parent];
            }
        }
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

    public void Pin(DateTimeOffset pinnedAt)
    {
        EnsureTimestampDoesNotMoveBackwards(pinnedAt);
        IsPinned = true;
        PinnedAt = pinnedAt;
        UpdatedAt = pinnedAt;
    }

    public void Unpin(DateTimeOffset updatedAt)
    {
        EnsureTimestampDoesNotMoveBackwards(updatedAt);
        IsPinned = false;
        PinnedAt = null;
        UpdatedAt = updatedAt;
    }

    /// <summary>Restores persisted pin state during deserialization, bypassing timestamp and revision bookkeeping.</summary>
    public void RestorePinState(bool isPinned, DateTimeOffset? pinnedAt)
    {
        IsPinned = isPinned;
        PinnedAt = isPinned ? pinnedAt : null;
    }

    public void AddMessage(ChatMessage message, DateTimeOffset updatedAt, Guid? branchId = null, Guid? parentBranchId = null)
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
            var parentBranch = parentBranchId is { } explicitParent
                ? _branches.GetValueOrDefault(explicitParent)
                : _branches.Values.FirstOrDefault(item => GetBranch(item.HeadMessageId).Any(parent => parent.Id == message.ParentId));
            if (parentBranchId is not null && parentBranch is null)
            {
                _messages.Remove(message.Id);
                throw new DomainException("Parent branch does not exist in this chat.");
            }
            var id = branchId ?? message.Id.Value;
            branch = new ChatBranch(id, message.ParentId, message.Content[..Math.Min(48, message.Content.Length)],
                parentBranch?.Id ?? Id.Value, message.Id);
        }
        else if (branch.HeadMessageId != message.ParentId)
        {
            _messages.Remove(message.Id);
            throw new DomainException("A message can only be appended to the current branch head.");
        }
        _branches[branch.Id] = branch with { HeadMessageId = message.Id, Revision = checked(branch.Revision + 1) };
        UpdatedAt = updatedAt;
        LastActivityAt = updatedAt;
    }

    public void ReplaceInBranch(Guid branchId, ChatMessageId sourceId, ChatMessage replacement, DateTimeOffset updatedAt)
    {
        if (!_branches.TryGetValue(branchId, out var branch)) throw new DomainException("Branch does not exist in this chat.");
        if (!_messages.TryGetValue(sourceId, out var source)) throw new DomainException("Replacement message does not exist in this chat.");
        if (replacement.ParentId != source.ParentId) throw new DomainException("A replacement must keep the source message parent.");
        if (!GetBranch(branch.HeadMessageId).Any(message => message.Id == sourceId))
            throw new DomainException("Replacement message does not belong to the selected branch.");
        EnsureTimestampDoesNotMoveBackwards(updatedAt);
        if (!_messages.TryAdd(replacement.Id, replacement)) throw new DomainException($"Chat message '{replacement.Id}' already exists.");
        _branches[branchId] = branch with { HeadMessageId = replacement.Id, Revision = checked(branch.Revision + 1) };
        UpdatedAt = updatedAt;
        LastActivityAt = updatedAt;
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

    public (Guid ParentBranchId, ChatMessageId? ParentHeadMessageId) DeleteBranch(Guid branchId, DateTimeOffset updatedAt)
    {
        if (branchId == Id.Value) throw new DomainException("The main branch cannot be deleted.");
        if (!_branches.TryGetValue(branchId, out var branch)) throw new DomainException("Branch does not exist in this chat.");
        var parentBranchId = branch.ParentBranchId ?? Id.Value;
        if (!_branches.TryGetValue(parentBranchId, out var parentBranch)) throw new DomainException("Parent branch does not exist in this chat.");
        EnsureTimestampDoesNotMoveBackwards(updatedAt);
        _branches.Remove(branchId);
        foreach (var child in _branches.Values.Where(item => item.ParentBranchId == branchId).ToArray())
            _branches[child.Id] = child with { ParentBranchId = parentBranchId, Revision = checked(child.Revision + 1) };
        UpdatedAt = updatedAt;
        return (parentBranchId, parentBranch.HeadMessageId);
    }

    public bool PruneUnreachableMessages(IEnumerable<ChatMessageId> retainedRoots)
    {
        var reachable = new HashSet<ChatMessageId>();
        var pending = new Stack<ChatMessageId>(_branches.Values.Select(branch => branch.HeadMessageId)
            .OfType<ChatMessageId>().Concat(retainedRoots));
        while (pending.TryPop(out var id))
        {
            if (!reachable.Add(id)) continue;
            if (_messages.TryGetValue(id, out var message) && message.ParentId is { } parent) pending.Push(parent);
        }
        var removed = _messages.Keys.Where(id => !reachable.Contains(id)).ToArray();
        foreach (var id in removed) _messages.Remove(id);
        return removed.Length > 0;
    }

    private void EnsureTimestampDoesNotMoveBackwards(DateTimeOffset updatedAt)
    {
        if (updatedAt < UpdatedAt)
        {
            throw new DomainException("Chat update timestamp cannot move backwards.");
        }
    }
}
