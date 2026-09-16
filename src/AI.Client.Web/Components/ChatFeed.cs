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
    /// One user turn as it appears in the transcript. <see cref="UserMessage"/> is null only for
    /// legacy/orphaned messages before the first user message. The final answer is deliberately
    /// separated from the intermediate items so the latter can be replaced by one compact row.
    /// </summary>
    public sealed record FeedTurn(
        ChatMessageView? UserMessage,
        IReadOnlyList<FeedItem> IntermediateItems,
        FeedItem? FinalAnswer)
    {
        public Guid? Id => UserMessage?.Id;

        public IEnumerable<ChatMessageView> IntermediateMessages =>
            IntermediateItems.SelectMany(MessagesOf);

        public ChatMessageView? LastMessage => FinalAnswer is { } answer
            ? MessagesOf(answer).LastOrDefault()
            : IntermediateMessages.LastOrDefault();
    }

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
    /// <summary>
    /// The last <paramref name="limit"/> items, in order — what a transcript shows before the
    /// rest of it has been rendered. The tail is the part the feed opens at, so it is the part
    /// worth paying for first; anything above it is reached by scrolling, which cannot happen in
    /// the frame that opens the chat.
    /// </summary>
    public static List<T> TakeTail<T>(List<T> items, int limit) =>
        limit >= items.Count ? items : items.GetRange(items.Count - Math.Max(limit, 0), Math.Max(limit, 0));

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
    /// Splits the rendered feed at user messages and identifies the last plain assistant message
    /// in each segment as its final answer. Earlier plain assistant messages remain intermediate:
    /// agent loops can emit several progress notes without tool calls before answering.
    /// </summary>
    public static List<FeedTurn> BuildTurns(
        IReadOnlyList<ChatMessageView> chain,
        bool lastTurnEndedWithoutFinalAnswer = false)
    {
        var turns = new List<FeedTurn>();
        ChatMessageView? user = null;
        var body = new List<FeedItem>();

        void FlushTurn()
        {
            if (user is null && body.Count == 0) return;

            FeedItem? finalAnswer = null;
            if (body.Count > 0 && IsPlainAssistantMessage(body[^1]))
            {
                finalAnswer = body[^1];
                body.RemoveAt(body.Count - 1);
            }

            turns.Add(new FeedTurn(user, body.ToArray(), finalAnswer));
            body = [];
        }

        foreach (var item in BuildFeedItems(chain))
        {
            if (item.Message is { Role: "User" } nextUser)
            {
                FlushTurn();
                user = nextUser;
            }
            else
            {
                body.Add(item);
            }
        }

        FlushTurn();

        // While a run is generating (or after it stopped/failed), its latest plain assistant
        // message is still a progress note. Shape alone cannot distinguish that note from a
        // completed answer, so the caller supplies the run-state fact and we put the message
        // back among the intermediate items.
        if (lastTurnEndedWithoutFinalAnswer
            && turns.Count > 0
            && turns[^1] is { UserMessage: not null, FinalAnswer: { } pendingAnswer } lastTurn)
        {
            turns[^1] = lastTurn with
            {
                IntermediateItems = [.. lastTurn.IntermediateItems, pendingAnswer],
                FinalAnswer = null
            };
        }

        return turns;
    }

    /// <summary>Returns the root-to-leaf message chain for the selected branch.</summary>
    public static IReadOnlyList<ChatMessageView> BuildBranch(
        IReadOnlyList<ChatMessageView> messages,
        Guid? branchLeafId)
    {
        if (branchLeafId is null) return messages.OrderBy(item => item.CreatedAt).ToArray();

        var byId = messages.ToDictionary(item => item.Id);
        var branch = new List<ChatMessageView>();
        var currentId = branchLeafId;
        while (currentId is { } messageId && byId.TryGetValue(messageId, out var message))
        {
            branch.Add(message);
            currentId = message.ParentId;
        }

        branch.Reverse();
        return branch;
    }

    /// <summary>
    /// The live compact title, when the model has supplied one. Kept here so the transcript row
    /// and composer status cannot disagree about which progress note is current.
    /// </summary>
    public static string? RunningTitleOf(FeedTurn turn, TimeSpan elapsed)
    {
        var action = turn.IntermediateMessages
            .LastOrDefault(message => message.Role == "Assistant" && !string.IsNullOrWhiteSpace(message.Content))
            ?.Content;
        return string.IsNullOrWhiteSpace(action)
            ? null
            : $"{SingleLine(action)} · {FormatDuration(elapsed)}";
    }

    public static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 1) return $"{elapsed.TotalMilliseconds:F0} ms";
        var totalSeconds = Math.Max(0, (int)elapsed.TotalSeconds);
        return totalSeconds < 60
            ? $"{totalSeconds}s"
            : $"{totalSeconds / 60}m {totalSeconds % 60}s";
    }

    private static string SingleLine(string content) =>
        string.Join(' ', content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static IEnumerable<ChatMessageView> MessagesOf(FeedItem item)
    {
        if (item.Message is { } message) yield return message;
        if (item.ToolGroup is not { } group) yield break;
        foreach (var groupedMessage in group) yield return groupedMessage;
    }

    private static bool IsPlainAssistantMessage(FeedItem item) =>
        item.Message is { Role: "Assistant", ToolCalls: not { Count: > 0 } };

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
