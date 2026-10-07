namespace AI.Web.Widgets;

using AI.Contracts.Chats;
using AI.Contracts.Runs;

/// <summary>Why one run counts as unfinished work that is still waiting for the person.</summary>
public enum ChatUnfinishedReason
{
    /// <summary>A tool approval is unanswered right now.</summary>
    Approval,

    /// <summary>The run asked a question and waits for the answer.</summary>
    Prompt,

    /// <summary>The run is waiting out a provider limit before it tries again.</summary>
    Wait,

    /// <summary>The run ended with a failure and its recovery actions are still open.</summary>
    Failed,

    /// <summary>The queue is paused and needs a person to resume it.</summary>
    Paused,

    /// <summary>The run stopped without finishing - closing the application leaves it in this state.</summary>
    Interrupted,

    /// <summary>The run is still producing its answer.</summary>
    Running,

    /// <summary>The run is finished or idle but messages are still queued behind it.</summary>
    Queue
}

/// <summary>One unfinished run in the project, as the client's own run snapshots report it.</summary>
/// <param name="ChatId">The chat the run belongs to.</param>
/// <param name="ChatTitle">The chat's stored title.</param>
/// <param name="BranchId">The branch the run works on; the main branch's id equals the chat id.</param>
/// <param name="IsMainBranch">True when the run's branch is the chat itself rather than a fork.</param>
/// <param name="Status">The run's own status, quoted exactly as the Host reported it.</param>
/// <param name="Reason">The most demanding thing the run is waiting for.</param>
/// <param name="NeedsAttention">True when the run cannot progress without the person, the same rule
/// the sidebar status badge uses.</param>
/// <param name="Queued">Messages still queued behind the run.</param>
/// <param name="CanResume">True when resuming is offered for this run: the Host lists a Resume
/// recovery action, or the queue only needs a pause lifted. A failure with no Resume among its
/// recovery actions opens the chat without a resume button rather than offering an action the Host
/// would refuse.</param>
/// <param name="LastActivityAt">When the chat itself was last active, from its stored summary. It is
/// when something was written, not when the run stopped, so the widget marks it approximate.</param>
public sealed record ChatUnfinishedTask(
    Guid ChatId,
    string ChatTitle,
    Guid BranchId,
    bool IsMainBranch,
    ChatRunStatus Status,
    ChatUnfinishedReason Reason,
    bool NeedsAttention,
    int Queued,
    bool CanResume,
    DateTimeOffset LastActivityAt);

/// <summary>The project's unfinished chat work: every run the client knows about that has not finished.</summary>
/// <param name="Tasks">One entry per unfinished run, the ones needing a person first.</param>
/// <param name="ChatsWithUnfinishedWork">How many distinct chats the tasks belong to.</param>
/// <param name="Resumable">How many tasks a resume action can be offered for.</param>
public sealed record ChatUnfinishedStatistics(
    IReadOnlyList<ChatUnfinishedTask> Tasks,
    int ChatsWithUnfinishedWork,
    int Resumable)
{
    public static ChatUnfinishedStatistics Empty { get; } = new([], 0, 0);

    public bool HasTasks => Tasks.Count > 0;
}

/// <summary>Builds the project's unfinished-work list from the run snapshots the client already holds.</summary>
public interface IChatUnfinishedStatisticsCalculator
{
    /// <param name="chats">The project's chats, as the sidebar knows them.</param>
    /// <param name="runs">Every run snapshot the client has, for the whole project.</param>
    ChatUnfinishedStatistics Calculate(IReadOnlyList<ChatSummary> chats, IReadOnlyList<ChatRunSnapshot> runs);
}

public sealed class ChatUnfinishedStatisticsCalculator : IChatUnfinishedStatisticsCalculator
{
    public ChatUnfinishedStatistics Calculate(IReadOnlyList<ChatSummary> chats, IReadOnlyList<ChatRunSnapshot> runs)
    {
        if (runs.Count == 0) return ChatUnfinishedStatistics.Empty;

        var byId = new Dictionary<Guid, ChatSummary>(chats.Count);
        foreach (var chat in chats)
        {
            // An empty chat has nothing to run and an archived one is out of the way: neither is
            // unfinished work, even if a stale run snapshot still names it.
            if (chat.IsEmpty || chat.ArchivedAt is not null) continue;
            byId[chat.Id] = chat;
        }

        var tasks = new List<ChatUnfinishedTask>();
        foreach (var run in runs)
        {
            if (!byId.TryGetValue(run.ChatId, out var chat)) continue;
            if (Describe(run) is not { } described) continue;
            tasks.Add(new ChatUnfinishedTask(
                run.ChatId,
                chat.Title,
                run.BranchId,
                run.BranchId == run.ChatId,
                run.Status,
                described.Reason,
                described.NeedsAttention,
                run.Queue.Count,
                CanResume(run),
                chat.LastActivityAt));
        }

        tasks.Sort(Compare);
        var chatsWithWork = tasks.Select(task => task.ChatId).Distinct().Count();
        return new ChatUnfinishedStatistics(tasks, chatsWithWork, tasks.Count(task => task.CanResume));
    }

    // Most demanding first: what blocks a person now, then what stopped and waits, then what is
    // still running, then work queued behind an idle chat. Ties go by chat title, then branch.
    private static int Compare(ChatUnfinishedTask left, ChatUnfinishedTask right)
    {
        var byRank = Rank(left).CompareTo(Rank(right));
        if (byRank != 0) return byRank;
        var byTitle = string.Compare(left.ChatTitle, right.ChatTitle, StringComparison.OrdinalIgnoreCase);
        if (byTitle != 0) return byTitle;
        return left.BranchId.CompareTo(right.BranchId);
    }

    private static int Rank(ChatUnfinishedTask task) => task.Reason switch
    {
        ChatUnfinishedReason.Approval or ChatUnfinishedReason.Prompt or ChatUnfinishedReason.Wait => 0,
        ChatUnfinishedReason.Failed => 1,
        ChatUnfinishedReason.Paused or ChatUnfinishedReason.Interrupted => 2,
        ChatUnfinishedReason.Running => 3,
        _ => 4
    };

    /// <summary>
    /// What, if anything, keeps this run unfinished, and whether the person has to act. The set
    /// matches the Host's own "busy" rule for a chat, so this widget and the archive cleanup agree
    /// on what still has to finish. The attention flag follows the same rule the run status
    /// presentation uses: a failure with no recovery actions left is over, not waiting.
    /// </summary>
    private static (ChatUnfinishedReason Reason, bool NeedsAttention)? Describe(ChatRunSnapshot run)
    {
        if (run.PendingApproval is not null) return (ChatUnfinishedReason.Approval, true);
        if (run.PendingPrompt is not null) return (ChatUnfinishedReason.Prompt, true);
        if (run.Wait is not null) return (ChatUnfinishedReason.Wait, true);
        if (run.Status == ChatRunStatus.Failed && run.RecoveryActions is { Count: > 0 })
            return (ChatUnfinishedReason.Failed, true);
        if (run.Status == ChatRunStatus.Paused) return (ChatUnfinishedReason.Paused, true);
        if (run.Status == ChatRunStatus.Interrupted) return (ChatUnfinishedReason.Interrupted, true);
        if (run.Status == ChatRunStatus.Generating) return (ChatUnfinishedReason.Running, false);
        if (run.Queue.Count > 0) return (ChatUnfinishedReason.Queue, false);
        // A failure with no recovery action left, or a finished run: nothing waits, nothing is offered.
        return null;
    }

    private static bool CanResume(ChatRunSnapshot run) =>
        run.RecoveryActions?.Contains(RunRecoveryAction.Resume) == true
        || run.Status is ChatRunStatus.Paused or ChatRunStatus.Interrupted
        || run.Queue.Count > 0;
}
