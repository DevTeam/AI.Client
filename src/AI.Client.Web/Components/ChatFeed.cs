using AI.Client.Contracts.Chat;
using AI.Client.Contracts.Chats;

namespace AI.Client.Web.Components;

/// <summary>
/// Projects a branch's flat message chain onto the items the transcript actually renders.
/// Lives outside MessageFeed.razor so the ordering rules below can be asserted directly:
/// the markup is only supposed to decide how an item looks, not which items exist.
/// </summary>
public static class ChatFeed
{
    /// <summary>
    /// Exactly one of <paramref name="Message"/> / <paramref name="ToolGroup"/> is set.
    /// </summary>
    public readonly record struct FeedItem(
        ChatMessageView? Message,
        IReadOnlyList<ChatMessageView>? ToolGroup);

    /// <summary>
    /// One call paired with its result, plus the window it actually occupied. The agent runs a
    /// group's calls in order and persists each result as it lands, so a call's elapsed time is
    /// the gap between the previous landing and its own — an observed number, not an estimate.
    /// </summary>
    public readonly record struct ToolInvocation(
        ChatToolCall Call,
        ChatMessageView? Result,
        DateTimeOffset StartedAt,
        DateTimeOffset? CompletedAt)
    {
        public TimeSpan? Duration => CompletedAt is { } completed && completed > StartedAt
            ? completed - StartedAt
            : null;
    }

    public static bool IsToolActivity(ChatMessageView message) =>
        message.Role == "Tool" || (message.Role == "Assistant" && message.ToolCalls is { Count: > 0 });

    /// <summary>
    /// An assistant message that carries both explicit content and tool calls. The model wrote
    /// that text for the user (a progress note before a slow call), so it stays visible; only its
    /// calls are folded into the tool group. Hidden reasoning never reaches this method — it is
    /// not stored as message content.
    /// </summary>
    public static bool IsPreamble(ChatMessageView message) =>
        message.Role == "Assistant"
        && message.ToolCalls is { Count: > 0 }
        && !string.IsNullOrWhiteSpace(message.Content);

    /// <summary>
    /// Collapses each run of consecutive tool-call/tool-result messages into one group, so a
    /// multi-call turn doesn't render as a stack of near-empty bubbles. A preamble starts a new
    /// group rather than joining the previous one, which gives
    /// <c>preamble + its tools → preamble + its tools → final answer</c>: each cycle is one block
    /// reading intent first, actions under it.
    /// </summary>
    public static List<FeedItem> BuildFeedItems(IReadOnlyList<ChatMessageView> chain)
    {
        var items = new List<FeedItem>();
        List<ChatMessageView>? currentGroup = null;

        void FlushGroup()
        {
            if (currentGroup is null) return;
            items.Add(new FeedItem(null, currentGroup));
            currentGroup = null;
        }

        foreach (var message in chain)
        {
            if (!IsToolActivity(message))
            {
                FlushGroup();
                items.Add(new FeedItem(message, null));
                continue;
            }

            // Without this flush a preamble would be swallowed by the previous cycle's group and
            // surface after the tools it announced, instead of opening its own.
            if (IsPreamble(message)) FlushGroup();

            currentGroup ??= [];
            currentGroup.Add(message);
        }

        FlushGroup();
        return items;
    }

    /// <summary>
    /// The assistant's explanation that opened this group, if it wrote one. Always the group's
    /// first message: a preamble is what starts a group (see <see cref="BuildFeedItems"/>).
    /// </summary>
    public static ChatMessageView? PreambleOf(IReadOnlyList<ChatMessageView> group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.Count > 0 && IsPreamble(group[0]) ? group[0] : null;
    }

    /// <summary>
    /// Pairs each call in the group with its result message (matched by ToolCallId) so a call
    /// still awaiting its result — the run is mid-flight — renders as running instead of nothing,
    /// and timestamps each one from the messages themselves.
    /// </summary>
    public static List<ToolInvocation> BuildInvocations(IReadOnlyList<ChatMessageView> group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var resultsByCallId = group
            .Where(item => item.Role == "Tool" && item.ToolCallId is not null)
            .ToDictionary(item => item.ToolCallId!, item => item);
        var invocations = new List<ToolInvocation>();
        foreach (var message in group)
        {
            if (message.ToolCalls is not { Count: > 0 }) continue;
            // The assistant message is durable intent recorded before any side effect, so the
            // first call of a batch starts there; each later one starts where the previous ended.
            var cursor = message.CreatedAt;
            foreach (var call in message.ToolCalls)
            {
                resultsByCallId.TryGetValue(call.Id, out var result);
                invocations.Add(new ToolInvocation(call, result, cursor, result?.CreatedAt));
                if (result is not null) cursor = result.CreatedAt;
            }
        }
        return invocations;
    }
}
