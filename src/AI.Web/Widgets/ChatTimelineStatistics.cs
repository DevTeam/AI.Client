namespace AI.Web.Widgets;

using AI.Contracts.Chats;
using AI.Contracts.Usage;

/// <summary>One turn on the visible branch, as the Timeline widget renders it.</summary>
/// <param name="TurnIndex">1-based position of the turn on the branch.</param>
/// <param name="UserMessageId">
/// Id of the user message that starts the turn; the transcript can be scrolled to it. Null for the
/// opening assistant turn that precedes the first user message.
/// </param>
/// <param name="StartedAt">
/// When the first message of the turn was recorded. Approximate: a long pause before the user
/// pressed Send counts as turn time, not idle.
/// </param>
/// <param name="DurationMs">
/// Time from the first to the last message of the turn, computed from message timestamps. Marked
/// approximate the same way Performance marks wall-clock.
/// </param>
/// <param name="HasDuration">False when the turn has only one message and a duration would be 0.</param>
/// <param name="Requests">Provider-reported request count for the turn, or null when the ledger has none.</param>
/// <param name="InputTokens">Input tokens of the turn, or null.</param>
/// <param name="OutputTokens">Output tokens of the turn, or null.</param>
/// <param name="ToolCalls">Distinct tool call names issued during the turn, in the order they first appeared.</param>
/// <param name="FilesChanged">Distinct file paths changed during the turn, taken from saved receipts.</param>
/// <param name="IsRunning">True while this turn is the live one and the run is still going.</param>
public sealed record TimelineTurn(
    int TurnIndex,
    Guid? UserMessageId,
    DateTimeOffset StartedAt,
    long DurationMs,
    bool HasDuration,
    int? Requests,
    long? InputTokens,
    long? OutputTokens,
    IReadOnlyList<string> ToolCalls,
    IReadOnlyList<string> FilesChanged,
    bool IsRunning);

/// <summary>
/// All turns on the visible branch, oldest first, in a shape the Timeline widget renders.
/// Durations and timestamps come from message <c>CreatedAt</c> (approximate); request and token
/// figures come from the per-turn slices of <see cref="ChatTokenUsage.Turns"/>. The two sources
/// are aligned by position: the latest ledger entry is the latest branch turn.
/// </summary>
/// <param name="Turns">One entry per turn on the visible branch.</param>
/// <param name="HasProviderData">True when at least one turn has a per-turn usage slice.</param>
public sealed record ChatTimelineStatistics(
    IReadOnlyList<TimelineTurn> Turns,
    bool HasProviderData)
{
    public static ChatTimelineStatistics Empty { get; } = new([], false);

    public bool HasTurns => Turns.Count > 0;
}

/// <summary>
/// Builds a <see cref="ChatTimelineStatistics"/> for the visible branch. Provider usage is matched
/// to the branch by position (last ledger entry → last branch turn), so the figures belong to the
/// right turn without needing to align ids — the same rule the Subtasks widget uses to count
/// turns with delegated work.
/// </summary>
public interface IChatTimelineStatisticsCalculator
{
    ChatTimelineStatistics Calculate(
        IReadOnlyList<ChatMessageView> branch,
        ChatTokenUsage? chatUsage,
        TurnTokenUsage? liveTurn,
        bool isRunning);
}

public sealed class ChatTimelineStatisticsCalculator : IChatTimelineStatisticsCalculator
{
    public ChatTimelineStatistics Calculate(
        IReadOnlyList<ChatMessageView> branch,
        ChatTokenUsage? chatUsage,
        TurnTokenUsage? liveTurn,
        bool isRunning)
    {
        if (branch.Count == 0) return ChatTimelineStatistics.Empty;
        var branchTurns = SplitTurns(branch);
        if (branchTurns.Count == 0) return ChatTimelineStatistics.Empty;

        // The ledger's per-turn slices are in turn order, oldest first. Match them by position
        // against the branch turns; the last branch turn is the live one and may not have a
        // stored slice yet, so the live turn's slice is appended at the end when it exists.
        var storedSlices = chatUsage?.Turns ?? [];
        var slices = new List<TurnTokenUsage?>(branchTurns.Count);
        for (var index = 0; index < branchTurns.Count; index++)
        {
            TurnTokenUsage? slice = null;
            if (index < storedSlices.Count) slice = storedSlices[index];
            // The live turn replaces the last stored slice when it starts no earlier and the
            // request count is at least as high — the same rule Performance and Subtasks use.
            else if (index == branchTurns.Count - 1 && liveTurn is not null
                && (storedSlices.Count == 0 || liveTurn.StartedAt >= storedSlices[^1].StartedAt))
            {
                slice = liveTurn;
            }
            slices.Add(slice);
        }

        var hasProvider = slices.Any(slice => slice is not null);
        var result = new List<TimelineTurn>(branchTurns.Count);
        for (var index = 0; index < branchTurns.Count; index++)
        {
            var turn = branchTurns[index];
            result.Add(BuildTurn(index + 1, turn, slices[index], isRunning && index == branchTurns.Count - 1));
        }
        return new ChatTimelineStatistics(result, hasProvider);
    }

    private static TimelineTurn BuildTurn(int turnIndex, List<ChatMessageView> turn, TurnTokenUsage? slice, bool isRunning)
    {
        var userMessage = turn.FirstOrDefault(message => message.Role == "User");
        var first = turn[0];
        var last = turn[^1];
        var startedAt = userMessage?.CreatedAt ?? first.CreatedAt;
        var durationMs = (long)(last.CreatedAt - first.CreatedAt).TotalMilliseconds;
        var hasDuration = turn.Count > 1 && durationMs > 0;

        var toolCalls = new List<string>();
        var seenTools = new HashSet<string>(StringComparer.Ordinal);
        var filesChanged = new List<string>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var message in turn)
        {
            if (message.Role == "Assistant" && message.ToolCalls is not null)
            {
                foreach (var call in message.ToolCalls)
                {
                    var label = DisplayToolName(call.Name);
                    if (seenTools.Add(label)) toolCalls.Add(label);
                }
            }
            if (message.WorkspaceChanges is { IsEmpty: false } changes)
            {
                foreach (var change in changes.Files)
                {
                    if (seenFiles.Add(change.Path)) filesChanged.Add(change.Path);
                }
            }
        }

        return new TimelineTurn(
            TurnIndex: turnIndex,
            UserMessageId: userMessage?.Id,
            StartedAt: startedAt,
            DurationMs: Math.Max(0, durationMs),
            HasDuration: hasDuration,
            Requests: slice?.Totals.Requests,
            InputTokens: slice?.Totals.Tokens.InputTokens,
            OutputTokens: slice?.Totals.Tokens.OutputTokens,
            ToolCalls: toolCalls,
            FilesChanged: filesChanged,
            IsRunning: isRunning);
    }

    // A tool call's name comes in as either a bare id (read_text_file) or with a server prefix
    // (mcp_files__read). Show the part a person recognises — the same form the Tools widget uses.
    private static string DisplayToolName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var underscore = name.LastIndexOf("__", StringComparison.Ordinal);
        return underscore < 0 ? name : name[(underscore + 2)..];
    }

    // A turn starts at a user message; whatever comes before the first one belongs to it.
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
}
