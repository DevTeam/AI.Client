using AI.Contracts.Chat;
using AI.Contracts.Chats;

namespace AI.Web.Components;

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
        IntermediateItems.SelectMany(ChatFeed.MessagesOf);

    public ChatMessageView? LastMessage => FinalAnswer is { } answer
        ? ChatFeed.MessagesOf(answer).LastOrDefault()
        : IntermediateMessages.LastOrDefault();
}

/// <summary>
/// A stretch of a turn's intermediate steps, closed by what the person put into the turn: a
/// message added during it, or an answer to the model's question. The pin is drawn after the
/// stretch whether it is folded or not, so folding and unfolding never move it past a step.
/// </summary>
public sealed record TurnSegment(int Index, IReadOnlyList<FeedItem> Items, ChatMessageView? Pin)
{
    public IEnumerable<ChatMessageView> Messages => Items.SelectMany(ChatFeed.MessagesOf);
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

/// <summary>A completed context checkpoint shown only among expanded intermediate steps.</summary>
public readonly record struct ContextCheckpoint(string CallId, int CoveredMessages);

/// <summary>The newest plan along a chain, and whether the chain's last turn is the one that set it.</summary>
public readonly record struct PlanSighting(AI.Contracts.Plans.ChatPlan Plan, bool InLastTurn);
