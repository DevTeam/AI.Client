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
    /// Exactly one of <paramref name="Message"/> / <paramref name="ToolGroup"/> is set. A message
    /// item with <paramref name="IsPreamble"/> is an assistant message that explained itself
    /// before issuing tool calls: its text belongs in the transcript, its calls belong to the
    /// tool group that follows it.
    /// </summary>
    public readonly record struct FeedItem(
        ChatMessageView? Message,
        IReadOnlyList<ChatMessageView>? ToolGroup,
        bool IsPreamble = false);

    public readonly record struct ToolPair(ChatToolCall Call, ChatMessageView? Result);

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
    /// multi-call turn doesn't render as a stack of near-empty bubbles — but lifts every preamble
    /// back out in front of the group it opens, giving
    /// <c>preamble → tools → preamble → tools → final answer</c>.
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

            // A preamble starts a new call cycle, so the preceding cycle's group is closed before
            // it: without this flush the text would surface after the tools it announced.
            if (IsPreamble(message))
            {
                FlushGroup();
                items.Add(new FeedItem(message, null, IsPreamble: true));
            }

            currentGroup ??= [];
            currentGroup.Add(message);
        }

        FlushGroup();
        return items;
    }

    /// <summary>
    /// Pairs each call in the group with its result message (matched by ToolCallId) so a call
    /// still awaiting its result — the run is mid-flight — renders as "Running…" instead of nothing.
    /// </summary>
    public static List<ToolPair> BuildToolPairs(IReadOnlyList<ChatMessageView> group)
    {
        var resultsByCallId = group
            .Where(item => item.Role == "Tool" && item.ToolCallId is not null)
            .ToDictionary(item => item.ToolCallId!, item => item);
        var pairs = new List<ToolPair>();
        foreach (var message in group)
        {
            if (message.ToolCalls is not { Count: > 0 }) continue;
            foreach (var call in message.ToolCalls)
            {
                resultsByCallId.TryGetValue(call.Id, out var result);
                pairs.Add(new ToolPair(call, result));
            }
        }
        return pairs;
    }
}
