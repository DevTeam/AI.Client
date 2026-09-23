using AI.Client.Contracts.Chats;
using AI.Client.Contracts.Tools;
using System.Text.Json;

namespace AI.Client.Web.Components;

/// <summary>
/// Projects a branch's flat message chain onto the items the transcript actually renders.
/// Lives outside MessageFeed.razor so the ordering rules below can be asserted directly:
/// the markup is only supposed to decide how an item looks, not which items exist.
/// </summary>
public sealed class ChatFeed : IChatFeedProjection
{
    public bool IsToolActivity(ChatMessageView message) =>
        message.Role == "Tool" || (message.Role == "Assistant" && message.ToolCalls is { Count: > 0 });

    /// <summary>
    /// An assistant message that carries both explicit content and tool calls. The model wrote
    /// that text for the user (a progress note before a slow call), so it stays visible; only its
    /// calls are folded into the tool group. Hidden reasoning never reaches this method — it is
    /// not stored as message content.
    /// </summary>
    public bool IsPreamble(ChatMessageView message) =>
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
    public List<T> TakeTail<T>(List<T> items, int limit) =>
        limit >= items.Count ? items : items.GetRange(items.Count - Math.Max(limit, 0), Math.Max(limit, 0));

    public List<FeedItem> BuildFeedItems(IReadOnlyList<ChatMessageView> chain)
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
    public List<FeedTurn> BuildTurns(
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
    public IReadOnlyList<ChatMessageView> BuildBranch(
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
    /// Whether the newest user turn already contains the durable workspace-change receipt. Chat
    /// and run events are published independently, so the receipt can arrive in the transcript
    /// before the run snapshot drops its live copy. Rendering both would show two statistics for
    /// the same edits during that transition.
    /// </summary>
    public bool LastTurnHasWorkspaceReceipt(IReadOnlyList<ChatMessageView> chain)
    {
        for (var index = chain.Count - 1; index >= 0; index--)
        {
            var message = chain[index];
            if (message.Role == "User") return false;
            if (message.WorkspaceChanges is { IsEmpty: false }) return true;
        }
        return false;
    }

    /// <summary>
    /// Whether the transcript already ends in a complete final answer. This durable fact takes
    /// precedence over a slightly older Generating snapshot during completion publication.
    /// </summary>
    public bool LastTurnHasCompleteAnswer(IReadOnlyList<ChatMessageView> chain) =>
        chain.Count > 0 && chain[^1] is
        {
            Role: "Assistant",
            IsIncomplete: false,
            ToolCalls: not { Count: > 0 }
        };

    /// <summary>
    /// The live compact title, when the model has supplied one. Kept here so the transcript row
    /// and composer status cannot disagree about which progress note is current.
    /// </summary>
    public string? RunningTitleOf(FeedTurn turn, TimeSpan elapsed)
    {
        var action = turn.IntermediateMessages
            .LastOrDefault(message => message.Role == "Assistant" && !string.IsNullOrWhiteSpace(message.Content))
            ?.Content;
        return string.IsNullOrWhiteSpace(action)
            ? null
            : $"{SingleLine(action)} · {FormatDuration(elapsed)}";
    }

    public string FormatDuration(TimeSpan elapsed)
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
    public ChatMessageView? PreambleOf(IReadOnlyList<ChatMessageView> group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.Count > 0 && IsPreamble(group[0]) ? group[0] : null;
    }

    /// <summary>
    /// Pairs each call in the group with its result message (matched by ToolCallId) so a call
    /// still awaiting its result — the run is mid-flight — renders as running instead of nothing,
    /// and timestamps each one from the messages themselves.
    /// </summary>
    public List<ToolInvocation> BuildInvocations(IReadOnlyList<ChatMessageView> group)
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

    public IReadOnlyList<ContextCheckpoint> CheckpointsOf(IReadOnlyList<ChatMessageView> group)
    {
        var checkpoints = new List<ContextCheckpoint>();
        foreach (var invocation in BuildInvocations(group))
        {
            if (ToolRef.Parse(invocation.Call.Name) is not { IsApp: true, Name: "context_compact" }
                || invocation.Result is not { ContentOmitted: false } result) continue;

            try
            {
                using var document = JsonDocument.Parse(result.Content);
                var envelope = document.RootElement;
                if (envelope.ValueKind != JsonValueKind.Object
                    || !envelope.TryGetProperty("isError", out var isError)
                    || isError.ValueKind != JsonValueKind.False
                    || !envelope.TryGetProperty("structuredContent", out var payload)
                    || payload.ValueKind != JsonValueKind.Object
                    || !payload.TryGetProperty("action", out var action)
                    || action.ValueKind != JsonValueKind.String
                    || action.GetString() != "Compact"
                    || !payload.TryGetProperty("applied", out var applied)
                    || applied.ValueKind != JsonValueKind.True
                    || !payload.TryGetProperty("coveredMessages", out var covered)
                    || covered.ValueKind != JsonValueKind.Number
                    || !covered.TryGetInt32(out var count)
                    || count <= 0) continue;

                checkpoints.Add(new ContextCheckpoint(invocation.Call.Id, count));
            }
            catch (JsonException) { /* Old or malformed tool output is ordinary tool activity. */ }
        }
        return checkpoints;
    }
}
