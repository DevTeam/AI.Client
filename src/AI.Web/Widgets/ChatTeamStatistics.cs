namespace AI.Web.Widgets;

using AI.Contracts.Chats;

/// <summary>How many team messages in scope carried one intent.</summary>
/// <param name="Intent">
/// The intent as the sender's run named it, lower-cased; null for a message sent without one. An
/// intent this build does not know is kept as it arrived rather than folded into a bucket.
/// </param>
/// <param name="Count">Messages in scope with that intent.</param>
public sealed record ChatTeamIntentCount(string? Intent, int Count);

/// <summary>One branch that took part in team messaging with the visible branch.</summary>
/// <param name="BranchId">The sending branch, as its run reported it.</param>
/// <param name="Title">
/// The branch's stored title, falling back to <c>Branch</c> when the chat stores none — a branch
/// whose title is unknown is still listed rather than dropped.
/// </param>
/// <param name="Messages">Messages in scope this branch sent into the visible branch.</param>
/// <param name="Asides">
/// How many of them arrived as asides. They cost the receiving branch no turn: the turn in flight
/// reads them at its next step, or they follow its reply.
/// </param>
/// <param name="Intents">What this branch's messages were, most frequent first.</param>
/// <param name="LastAt">
/// When this branch's latest message in scope was recorded. Approximate: it comes from the message
/// timestamp, which is when the message was stored, not when the sending branch wrote it.
/// </param>
/// <param name="LastMessageId">That latest message; the transcript scrolls to it.</param>
public sealed record ChatTeamBranchSummary(
    Guid BranchId,
    string Title,
    int Messages,
    int Asides,
    IReadOnlyList<ChatTeamIntentCount> Intents,
    DateTimeOffset LastAt,
    Guid LastMessageId);

/// <summary>
/// The team a branch is working with: every branch whose run sent a message into this one, in the
/// chosen scope. A branch that never sent anything does not appear — the widget reports what
/// happened, not who was invited.
/// </summary>
/// <param name="Branches">Participating branches, most messages first.</param>
/// <param name="Messages">Team messages in scope, from every sending branch together.</param>
/// <param name="Intents">Intent totals for the scope, in the protocol's order.</param>
/// <param name="Turns">How many turns the scope covers; the same count the other widgets report.</param>
/// <param name="TurnsWithMessages">Turns in which at least one team message arrived.</param>
/// <param name="IsRunning">True while the scope's last turn is still going; the widget says so far.</param>
public sealed record ChatTeamStatistics(
    IReadOnlyList<ChatTeamBranchSummary> Branches,
    int Messages,
    IReadOnlyList<ChatTeamIntentCount> Intents,
    int Turns,
    int TurnsWithMessages,
    bool IsRunning)
{
    public static ChatTeamStatistics Empty { get; } = new([], 0, [], 0, 0, false);

    public bool HasBranches => Branches.Count > 0;
}

/// <summary>
/// Builds the team figures for the visible branch. The only source is
/// <see cref="ChatMessageView.Sender"/>, which the Host fills in from the sending run's own context
/// and no client can forge: a message without a sender was not sent by another branch and does not
/// count. Nothing is inferred from message text, from the branch list, or from timestamps beyond the
/// arrival time each message already carries.
/// </summary>
public interface IChatTeamStatisticsCalculator
{
    /// <param name="branch">The visible branch, root to leaf.</param>
    /// <param name="branches">The chat's stored branches, used only to name the sending branch.</param>
    /// <param name="isRunning">True while the last turn is still going; the widget says so far.</param>
    /// <param name="scope">Whole chat or last turn.</param>
    ChatTeamStatistics Calculate(
        IReadOnlyList<ChatMessageView> branch,
        IReadOnlyList<ChatBranchView>? branches,
        bool isRunning,
        ChatWidgetScope scope);
}

public sealed class ChatTeamStatisticsCalculator : IChatTeamStatisticsCalculator
{
    // The protocol's intents, in the order the team skills use them. Anything else keeps its own
    // place after these, so a newer intent is still reported instead of disappearing.
    private static readonly string[] IntentOrder = ["question", "answer", "decision", "blocker", "status", "done"];

    public ChatTeamStatistics Calculate(
        IReadOnlyList<ChatMessageView> branch,
        IReadOnlyList<ChatBranchView>? branches,
        bool isRunning,
        ChatWidgetScope scope)
    {
        var messageList = branch ?? [];
        var turns = SplitTurns(messageList);
        if (scope == ChatWidgetScope.LastTurn && turns.Count > 1) turns = turns[^1..];

        var byBranch = new Dictionary<Guid, Accumulator>();
        var intentTotals = new Dictionary<string, int>(StringComparer.Ordinal);
        var messages = 0;
        var turnsWithMessages = 0;

        foreach (var turn in turns)
        {
            var turnHadMessages = false;
            foreach (var message in turn)
            {
                if (message.Sender is not { } sender) continue;
                messages++;
                turnHadMessages = true;

                var intent = NormalizeIntent(sender.Intent);
                if (intent is not null)
                    intentTotals[intent] = intentTotals.TryGetValue(intent, out var known) ? known + 1 : 1;

                if (byBranch.TryGetValue(sender.BranchId, out var accumulator)) accumulator.Add(message, intent);
                else byBranch[sender.BranchId] = new Accumulator(message, intent);
            }
            if (turnHadMessages) turnsWithMessages++;
        }

        var rows = byBranch
            .Select(pair => pair.Value.ToSummary(pair.Key, ResolveTitle(branches, pair.Key)))
            .OrderByDescending(row => row.Messages)
            .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var intents = OrderIntents(intentTotals);
        return new ChatTeamStatistics(rows, messages, intents, turns.Count, turnsWithMessages,
            isRunning && scope == ChatWidgetScope.LastTurn);
    }

    // A branch's stored title is the only name the chat has for it; a branch that is not in the list
    // (deleted, or written by a build that stored no entry) is still shown, under the same fallback
    // the Branches widget uses.
    private static string ResolveTitle(IReadOnlyList<ChatBranchView>? branches, Guid branchId)
    {
        if (branches is not null)
        {
            foreach (var branch in branches)
            {
                if (branch.Id != branchId) continue;
                return string.IsNullOrWhiteSpace(branch.Title) ? "Branch" : branch.Title;
            }
        }
        return "Branch";
    }

    private static string? NormalizeIntent(string? intent) =>
        string.IsNullOrWhiteSpace(intent) ? null : intent.Trim().ToLowerInvariant();

    private static ChatTeamIntentCount[] OrderIntents(Dictionary<string, int> totals)
    {
        var ordered = new List<ChatTeamIntentCount>(totals.Count);
        foreach (var intent in IntentOrder)
            if (totals.TryGetValue(intent, out var count)) ordered.Add(new ChatTeamIntentCount(intent, count));

        var known = ordered.Select(item => item.Intent!).ToHashSet(StringComparer.Ordinal);
        var others = totals.Where(pair => !known.Contains(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ChatTeamIntentCount(pair.Key, pair.Value));
        ordered.AddRange(others);
        return [.. ordered];
    }

    // A turn starts at a person's message; whatever comes before the first one belongs to it. Every
    // widget that splits turns uses this rule, so "the last turn" means the same thing in all of them.
    private static List<List<ChatMessageView>> SplitTurns(IReadOnlyList<ChatMessageView> branch)
    {
        var turns = new List<List<ChatMessageView>>();
        foreach (var message in branch)
        {
            if (message.Role == "User" || turns.Count == 0) turns.Add([]);
            turns[^1].Add(message);
        }
        return turns;
    }

    private sealed class Accumulator(ChatMessageView message, string? intent)
    {
        private readonly Dictionary<string, int> _intents = new(StringComparer.Ordinal);
        private DateTimeOffset _lastAt = message.CreatedAt;
        private Guid _lastMessageId = message.Id;

        public int Messages { get; private set; } = 1;

        public int Asides { get; private set; } = message.Delivery == MessageDelivery.Aside ? 1 : 0;

        public void Add(ChatMessageView other, string? otherIntent)
        {
            Messages++;
            if (other.Delivery == MessageDelivery.Aside) Asides++;
            if (otherIntent is not null)
                _intents[otherIntent] = _intents.TryGetValue(otherIntent, out var known) ? known + 1 : 1;
            if (other.CreatedAt < _lastAt) return;
            _lastAt = other.CreatedAt;
            _lastMessageId = other.Id;
        }

        public ChatTeamBranchSummary ToSummary(Guid branchId, string title)
        {
            if (intent is not null) _intents[intent] = _intents.TryGetValue(intent, out var known) ? known + 1 : 1;
            var intents = OrderIntents(_intents);
            return new ChatTeamBranchSummary(branchId, title, Messages, Asides, intents, _lastAt, _lastMessageId);
        }
    }
}
