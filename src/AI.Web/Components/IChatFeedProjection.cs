using AI.Contracts.Chats;

namespace AI.Web.Components;

/// <summary>
/// Projects a branch's flat message chain onto the items the transcript actually renders.
/// Lives outside MessageFeed.razor so the ordering rules below can be asserted directly:
/// the markup is only supposed to decide how an item looks, not which items exist.
/// </summary>
public interface IChatFeedProjection
{
    bool IsToolActivity(ChatMessageView message);

    /// <summary>
    /// An assistant message that carries both explicit content and tool calls. The model wrote
    /// that text for the user (a progress note before a slow call), so it stays visible; only its
    /// calls are folded into the tool group. Hidden reasoning never reaches this method — it is
    /// not stored as message content.
    /// </summary>
    bool IsPreamble(ChatMessageView message);

    List<T> TakeTail<T>(List<T> items, int limit);

    List<FeedItem> BuildFeedItems(IReadOnlyList<ChatMessageView> chain);

    /// <summary>Actual answer model changes, ordered along the visible branch and keyed by user turn.</summary>
    IReadOnlyDictionary<Guid, IReadOnlyList<ModelSwitch>> BuildModelSwitches(IReadOnlyList<ChatMessageView> chain,
        IReadOnlyList<AI.Contracts.Usage.TurnTokenUsage> turns, AI.Contracts.Usage.TurnTokenUsage? liveTurn);

    /// <summary>
    /// Splits the rendered feed at user messages and identifies the last plain assistant message
    /// in each segment as its final answer. Earlier plain assistant messages remain intermediate:
    /// agent loops can emit several progress notes without tool calls before answering.
    /// </summary>
    List<FeedTurn> BuildTurns(
        IReadOnlyList<ChatMessageView> chain,
        bool lastTurnEndedWithoutFinalAnswer = false);

    /// <summary>Returns the root-to-leaf message chain for the selected branch.</summary>
    IReadOnlyList<ChatMessageView> BuildBranch(
        IReadOnlyList<ChatMessageView> messages,
        Guid? branchLeafId);

    /// <summary>
    /// Whether the newest user turn already contains the durable workspace-change receipt. Chat
    /// and run events are published independently, so the receipt can arrive in the transcript
    /// before the run snapshot drops its live copy. Rendering both would show two statistics for
    /// the same edits during that transition.
    /// </summary>
    bool LastTurnHasWorkspaceReceipt(IReadOnlyList<ChatMessageView> chain);

    /// <summary>
    /// Whether the transcript already ends in a complete final answer. This durable fact takes
    /// precedence over a slightly older Generating snapshot during completion publication.
    /// </summary>
    bool LastTurnHasCompleteAnswer(IReadOnlyList<ChatMessageView> chain);

    /// <summary>
    /// The live compact title, when the model has supplied one. Kept here so the transcript row
    /// and composer status cannot disagree about which progress note is current.
    /// </summary>
    string? RunningTitleOf(FeedTurn turn, TimeSpan elapsed);

    string FormatDuration(TimeSpan elapsed);

    /// <summary>
    /// The assistant's explanation that opened this group, if it wrote one. Always the group's
    /// first message: a preamble is what starts a group (see <see cref="BuildFeedItems"/>).
    /// </summary>
    ChatMessageView? PreambleOf(IReadOnlyList<ChatMessageView> group);

    /// <summary>
    /// Pairs each call in the group with its result message (matched by ToolCallId) so a call
    /// still awaiting its result — the run is mid-flight — renders as running instead of nothing,
    /// and timestamps each one from the messages themselves.
    /// </summary>
    List<ToolInvocation> BuildInvocations(IReadOnlyList<ChatMessageView> group);

    /// <summary>Successful model-only context checkpoints recorded by this tool group.</summary>
    IReadOnlyList<ContextCheckpoint> CheckpointsOf(IReadOnlyList<ChatMessageView> group);
}
