namespace AI.Web.Widgets;

using AI.Contracts.Chats;

/// <summary>One branch as the Branches widget renders it.</summary>
/// <param name="RootId">The branch's own id, used to switch the visible branch on click.</param>
/// <param name="Title">The branch's stored title, falling back to <c>Branch</c> when empty.</param>
/// <param name="LeafId">
/// Last message on the branch. The page uses it as the leaf for <c>Feed.BuildBranch</c> when the
/// branch becomes visible; the widget itself only displays it in the summary tooltip.
/// </param>
/// <param name="Depth">
/// Distance from a root branch (one with no parent). 0 for a root, 1 for a child of a root, and so
/// on. Matches the depth the sidebar already computes in <c>BranchTreeItem</c>.
/// </param>
/// <param name="MessageCount">
/// How many messages belong to the branch — the root message plus every descendant reachable
/// through <see cref="ChatMessageView.ParentId"/>. Includes messages that may not yet have a
/// stored branch of their own.
/// </param>
/// <param name="ChildCount">How many other branches have this branch as their parent.</param>
/// <param name="HeadAt">
/// <see cref="DateTimeOffset"/> of the head message. Approximate: it comes from message
/// timestamps, so a long idle pause before the last message counts as part of the branch's age
/// rather than the user's. Null when the head message is missing.
/// </param>
/// <param name="IsActive">True when this branch is the one currently visible.</param>
public sealed record ChatBranchSummary(
    Guid RootId,
    string Title,
    Guid LeafId,
    int Depth,
    int MessageCount,
    int ChildCount,
    DateTimeOffset? HeadAt,
    bool IsActive);

/// <summary>
/// All branches of the chat, in the order they were stored. The chat itself is its own
/// implicit root branch, but it is only listed when the chat stores an explicit
/// <see cref="ChatBranchView"/> for it — the page owns the chat/branch distinction.
/// </summary>
/// <param name="Branches">One entry per stored branch.</param>
/// <param name="HasBranches">True when at least one branch is present.</param>
public sealed record ChatBranchesStatistics(
    IReadOnlyList<ChatBranchSummary> Branches,
    bool HasBranches)
{
    public static ChatBranchesStatistics Empty { get; } = new([], false);
}

/// <summary>
/// Builds a <see cref="ChatBranchesStatistics"/> for the chat. Message counts walk the message
/// tree from each branch's root through <see cref="ChatMessageView.ParentId"/>; head timestamps
/// come from the head message's <see cref="ChatMessageView.CreatedAt"/>. Anything that cannot be
/// inferred from the stored branches and messages — tool time, TTFT, branch-local token
/// accounting — is left out and the widget says so.
/// </summary>
public interface IChatBranchesStatisticsCalculator
{
    ChatBranchesStatistics Calculate(
        IReadOnlyList<ChatBranchView>? branches,
        IReadOnlyList<ChatMessageView> messages,
        Guid? selectedBranchId);
}

public sealed class ChatBranchesStatisticsCalculator : IChatBranchesStatisticsCalculator
{
    public ChatBranchesStatistics Calculate(
        IReadOnlyList<ChatBranchView>? branches,
        IReadOnlyList<ChatMessageView> messages,
        Guid? selectedBranchId)
    {
        if (branches is null || branches.Count == 0) return ChatBranchesStatistics.Empty;
        var messageList = messages ?? [];

        // parent → children, for the whole chat. Each child has exactly one parent in this tree,
        // so the index gives every descendant of any node without re-walking the list.
        var children = BuildChildrenIndex(messageList);
        var depthByBranch = ComputeDepths(branches);

        var rows = new List<ChatBranchSummary>(branches.Count);
        foreach (var branch in branches)
        {
            var root = ResolveRoot(branch, messageList);
            var count = root is null ? 0 : CountDescendants(root, children);
            rows.Add(new ChatBranchSummary(
                RootId: branch.Id,
                Title: string.IsNullOrWhiteSpace(branch.Title) ? "Branch" : branch.Title,
                LeafId: branch.HeadMessageId ?? branch.Id,
                Depth: depthByBranch.TryGetValue(branch.Id, out var depth) ? depth : 0,
                MessageCount: count,
                ChildCount: CountChildren(branches, branch.Id),
                HeadAt: ResolveHeadAt(branch, messageList),
                IsActive: selectedBranchId == branch.Id));
        }

        return new ChatBranchesStatistics(rows, true);
    }

    private static Dictionary<Guid, List<ChatMessageView>> BuildChildrenIndex(IReadOnlyList<ChatMessageView> messages)
    {
        var children = new Dictionary<Guid, List<ChatMessageView>>();
        foreach (var message in messages)
        {
            if (message.ParentId is not { } parent) continue;
            if (!children.TryGetValue(parent, out var list))
                children[parent] = list = new List<ChatMessageView>();
            list.Add(message);
        }
        return children;
    }

    // Walk from the head back to the root (the message whose parent is null). The stored
    // RootMessageId is preferred when it points to a known message; otherwise the walk decides.
    private static ChatMessageView? ResolveRoot(ChatBranchView branch, IReadOnlyList<ChatMessageView> messages)
    {
        if (branch.RootMessageId is { } rootId)
        {
            foreach (var message in messages)
                if (message.Id == rootId) return message;
        }
        if (branch.HeadMessageId is not { } headId) return null;
        var byId = new Dictionary<Guid, ChatMessageView>(messages.Count);
        foreach (var message in messages) byId[message.Id] = message;
        if (!byId.TryGetValue(headId, out var current)) return null;
        while (current is not null)
        {
            if (current.ParentId is null) return current;
            if (!byId.TryGetValue(current.ParentId.Value, out var parent)) return current;
            current = parent;
        }
        return null;
    }

    private static int CountDescendants(ChatMessageView root, Dictionary<Guid, List<ChatMessageView>> children)
    {
        var count = 0;
        var stack = new Stack<ChatMessageView>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            count++;
            if (children.TryGetValue(node.Id, out var kids))
                foreach (var kid in kids) stack.Push(kid);
        }
        return count;
    }

    private static DateTimeOffset? ResolveHeadAt(ChatBranchView branch, IReadOnlyList<ChatMessageView> messages)
    {
        if (branch.HeadMessageId is not { } headId) return null;
        foreach (var message in messages)
            if (message.Id == headId) return message.CreatedAt;
        return null;
    }

    private static int CountChildren(IReadOnlyList<ChatBranchView> branches, Guid branchId)
    {
        var count = 0;
        foreach (var other in branches)
            if (other.ParentBranchId == branchId) count++;
        return count;
    }

    // Depth = distance from a root branch (ParentBranchId == null). Walks up the parent chain;
    // the visited set guards against a malformed cycle (a branch whose ancestor chain loops).
    private static Dictionary<Guid, int> ComputeDepths(IReadOnlyList<ChatBranchView> branches)
    {
        var byId = new Dictionary<Guid, ChatBranchView>(branches.Count);
        foreach (var branch in branches) byId[branch.Id] = branch;

        var depth = new Dictionary<Guid, int>(branches.Count);
        foreach (var branch in branches)
        {
            var d = 0;
            var current = branch;
            var visited = new HashSet<Guid> { branch.Id };
            while (current.ParentBranchId is { } parentId
                && byId.TryGetValue(parentId, out var parent)
                && visited.Add(parent.Id))
            {
                if (depth.TryGetValue(parent.Id, out var inherited))
                {
                    d = inherited + 1;
                    break;
                }
                d++;
                current = parent;
            }
            depth[branch.Id] = d;
        }
        return depth;
    }
}
