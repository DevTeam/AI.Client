namespace AI.Domain.Chats;

using Common;
using Projects;
using System.Text.Json;

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
        ConnectionId? connectionId = null,
        bool autoTitlePending = false, ChatKind kind = default, JsonElement? kindState = null,
        int kindStateVersion = 1)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Chat title cannot be empty.");
        }
        if (kindStateVersion < 1) throw new DomainException("Chat kind state version must be positive.");

        Id = id;
        ProjectId = projectId;
        Title = title.Trim();
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        LastActivityAt = createdAt;
        ConnectionId = connectionId;
        AutoTitlePending = autoTitlePending;
        Kind = kind == default ? ChatKind.Conversation : kind;
        KindState = kindState?.Clone();
        KindStateVersion = kindStateVersion;
        _branches[id.Value] = new ChatBranch(id.Value, null, title.Trim());
    }

    public ChatId Id { get; }
    public ProjectId ProjectId { get; }
    public string Title { get; private set; }
    public bool AutoTitlePending { get; private set; }
    public ChatKind Kind { get; }
    public JsonElement? KindState { get; }
    public int KindStateVersion { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset LastActivityAt { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public Guid? ArchiveOperationId { get; private set; }
    public bool IsPinned { get; private set; }
    public DateTimeOffset? PinnedAt { get; private set; }
    /// <summary>The chat's place among the pinned chats of its project, as a key made by <c>IPinOrderKeys</c>;
    /// null when unpinned, and for chats pinned before manual ordering existed.</summary>
    public string? PinOrder { get; private set; }
    public IReadOnlyCollection<ChatMessage> Messages => _messages.Values;
    public ConnectionId? ConnectionId { get; private set; }
    public ChatApprovalMode ApprovalMode { get; private set; }
    public IReadOnlyCollection<ChatBranch> Branches => _branches.Values;

    /// <summary>
    /// Number of alternative branches a user can actually navigate to. The main branch is the
    /// chat itself, and a branch with no head message has no row in the branch tree yet, so
    /// neither is counted. Kept deliberately in step with the tree built by Home.razor's
    /// <c>ComputeBranchTreeItems</c>, which filters on exactly these two conditions — if the two
    /// ever disagree, the chat list advertises a branch row that cannot be opened.
    /// </summary>
    public int BranchCount => _branches.Values.Count(branch => branch.Id != Id.Value && branch.HeadMessageId is not null);
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
        AutoTitlePending = false;
        UpdatedAt = updatedAt;
    }

    public bool ApplyAutomaticTitle(string title, DateTimeOffset updatedAt)
    {
        if (!AutoTitlePending) return false;
        Rename(title, updatedAt);
        return true;
    }

    public void SetArchived(bool archived, Guid operationId, DateTimeOffset at)
    {
        EnsureTimestampDoesNotMoveBackwards(at);
        if ((ArchivedAt is not null) == archived) return;
        ArchivedAt = archived ? at : null;
        ArchiveOperationId = archived ? operationId : null;
        UpdatedAt = at;
    }

    public void RestoreArchiveState(DateTimeOffset? archivedAt, Guid? operationId)
    {
        ArchivedAt = archivedAt;
        ArchiveOperationId = archivedAt is null ? null : operationId;
    }

    public void RestoreAutoTitlePending(bool pending) => AutoTitlePending = pending;

    public void SetConnection(ConnectionId? connectionId, DateTimeOffset updatedAt)
    {
        EnsureTimestampDoesNotMoveBackwards(updatedAt);

        ConnectionId = connectionId;
        UpdatedAt = updatedAt;
    }

    public void SetApprovalMode(ChatApprovalMode mode, DateTimeOffset updatedAt)
    {
        EnsureTimestampDoesNotMoveBackwards(updatedAt);
        if (!Enum.IsDefined(mode)) throw new DomainException("Unknown approval mode.");
        ApprovalMode = mode;
        UpdatedAt = updatedAt;
    }

    public void RestoreApprovalMode(ChatApprovalMode mode) => ApprovalMode = Enum.IsDefined(mode) ? mode : ChatApprovalMode.Ask;

    public void Pin(string order, DateTimeOffset pinnedAt)
    {
        EnsureTimestampDoesNotMoveBackwards(pinnedAt);
        if (string.IsNullOrWhiteSpace(order)) throw new DomainException("A pin order cannot be empty.");
        // Moving a chat that is already pinned keeps the moment it was first pinned.
        if (!IsPinned) PinnedAt = pinnedAt;
        IsPinned = true;
        PinOrder = order;
        UpdatedAt = pinnedAt;
    }

    public void Unpin(DateTimeOffset updatedAt)
    {
        EnsureTimestampDoesNotMoveBackwards(updatedAt);
        IsPinned = false;
        PinnedAt = null;
        PinOrder = null;
        UpdatedAt = updatedAt;
    }

    /// <summary>Restores persisted pin state during deserialization, bypassing timestamp and revision bookkeeping.</summary>
    public void RestorePinState(bool isPinned, DateTimeOffset? pinnedAt, string? pinOrder = null)
    {
        IsPinned = isPinned;
        PinnedAt = isPinned ? pinnedAt : null;
        PinOrder = isPinned ? pinOrder : null;
    }

    /// <summary>
    /// Restores the persisted activity time. Replaying messages re-derives it, but a failed run
    /// marks activity without writing a message, and that moment exists only in the document.
    /// </summary>
    public void RestoreActivity(DateTimeOffset lastActivityAt)
    {
        if (lastActivityAt > LastActivityAt) LastActivityAt = lastActivityAt;
    }

    /// <summary>
    /// Records that something the user should look at happened without a message being written —
    /// a run that failed. It lifts the chat in the sidebar exactly like a reply would.
    /// </summary>
    public void MarkActivity(DateTimeOffset at)
    {
        EnsureTimestampDoesNotMoveBackwards(at);
        UpdatedAt = at;
        LastActivityAt = at;
    }

    /// <summary>
    /// Tool calls and their results are the agent's working steps, not something that happened in
    /// the conversation. Letting them count as activity made a long run keep jumping its chat to
    /// the top of the sidebar on every step; the user's message and the final reply mark it instead.
    /// </summary>
    private static bool IsActivity(ChatMessage message) =>
        message.Role != ChatMessageRole.Tool && message.ToolCalls is not { Count: > 0 };

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
        if (IsActivity(message)) LastActivityAt = updatedAt;
    }

    public bool RemoveReviewReferences(Guid reviewId, DateTimeOffset updatedAt)
    {
        var messageIds = _messages.Values.Where(message => message.Role == ChatMessageRole.User
            && message.Resources?.Any(item => item.Id == reviewId
                && item.Kind == AI.Domain.Resources.ChatResourceKind.Review) == true)
            .Select(message => message.Id).ToArray();
        foreach (var messageId in messageIds) RemoveReviewReference(messageId, reviewId, updatedAt);
        return messageIds.Length > 0;
    }

    private bool RemoveReviewReference(ChatMessageId messageId, Guid reviewId, DateTimeOffset updatedAt)
    {
        if (!_messages.TryGetValue(messageId, out var message) || message.Role != ChatMessageRole.User)
            return false;
        var resources = message.Resources;
        if (resources is null || !resources.Any(item => item.Id == reviewId
            && item.Kind == AI.Domain.Resources.ChatResourceKind.Review)) return false;
        EnsureTimestampDoesNotMoveBackwards(updatedAt);
        var remaining = resources.Where(item => item.Id != reviewId).ToArray();
        _messages[messageId] = new ChatMessage(message.Id, message.ParentId, message.Role,
            message.Content, message.CreatedAt, message.IsIncomplete, message.ToolCalls,
            message.ToolCallId, message.WorkspaceChanges, remaining, allowEmptyAfterResourceRemoval: true,
            message.Delivery, message.Sender);
        UpdatedAt = updatedAt;
        return true;
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
        // A replacement is a sibling of the message it replaces, not its descendant, so replacing the
        // message the fork starts at moves the fork point: the old root ends up on an abandoned line and
        // is no longer an ancestor of the new head. RestoreBranches enforces exactly that relation, so
        // leaving the root behind writes a chat that throws on the next load. Only re-root when the old
        // root actually fell off the branch — replacing a later message keeps the fork point intact.
        var rootMessageId = branch.RootMessageId is { } currentRoot
            && !GetBranch(replacement.Id).Any(message => message.Id == currentRoot)
                ? replacement.Id
                : branch.RootMessageId;
        _branches[branchId] = branch with
        {
            HeadMessageId = replacement.Id, RootMessageId = rootMessageId, Revision = checked(branch.Revision + 1)
        };
        UpdatedAt = updatedAt;
        if (IsActivity(replacement)) LastActivityAt = updatedAt;
    }

    /// <summary>
    /// Moves a branch head back to an earlier message on the same branch, abandoning everything
    /// after it. Used to throw away an attempt that was interrupted: its truncated answer and any
    /// tool messages it wrote are left unreachable, for <see cref="PruneUnreachableMessages"/> to
    /// collect. Returns false when the head is already there, so a caller can skip the save.
    /// </summary>
    public bool RewindBranchTo(Guid branchId, ChatMessageId head, DateTimeOffset updatedAt)
    {
        if (!_branches.TryGetValue(branchId, out var branch)) throw new DomainException("Branch does not exist in this chat.");
        if (!_messages.ContainsKey(head)) throw new DomainException("Branch message does not exist in this chat.");
        if (branch.HeadMessageId == head) return false;
        if (!GetBranch(branch.HeadMessageId).Any(message => message.Id == head))
            throw new DomainException("A branch can only be rewound to a message it already contains.");
        // RestoreBranches refuses a branch whose root is not an ancestor of its head, so rewinding
        // past the fork point would write a chat that throws on the next load.
        if (branch.RootMessageId is { } root && !GetBranch(head).Any(message => message.Id == root))
            throw new DomainException("A branch cannot be rewound past its own root.");
        EnsureTimestampDoesNotMoveBackwards(updatedAt);
        _branches[branchId] = branch with { HeadMessageId = head, Revision = checked(branch.Revision + 1) };
        UpdatedAt = updatedAt;
        return true;
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
