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

    /// <summary>The run stopped without finishing and the Host recorded nothing about why.</summary>
    Interrupted,

    /// <summary>A restart of the application stopped the run: it was closed, or stopped without closing.</summary>
    Restart,

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
/// <param name="CanResume">True when resuming is what the run is waiting for: it is paused or
/// interrupted, it failed and the Host lists Resume among its recovery actions, or the chat is idle
/// with messages queued behind it. A run that waits for an answer, waits out a limit or is still
/// generating is never offered a resume: resuming would not move it, so its row opens the chat.</param>
/// <param name="LastActivityAt">When the chat itself was last active, from its stored summary. It is
/// when something was written, not when the run stopped, so the widget marks it approximate.</param>
/// <param name="Detail">What exactly the run waits on, in a few words when the snapshot says it: the
/// tool asking for approval, the question asked, the failure message, or the next queued message.
/// Null when the snapshot carries nothing more than the reason itself.</param>
/// <param name="RetryAt">When a run waiting out a provider limit tries again on its own.</param>
/// <param name="ProjectId">The project the chat belongs to, so a task of another project is resumed there.</param>
/// <param name="Interruption">Why a <see cref="ChatUnfinishedReason.Restart"/> task stopped.</param>
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
    DateTimeOffset LastActivityAt,
    string? Detail = null,
    DateTimeOffset? RetryAt = null,
    Guid ProjectId = default,
    RunInterruption? Interruption = null)
{
    /// <summary>The restart left a tool call of this run without a result, so what it did is unknown.</summary>
    public bool HasUnknownToolOutcome => Interruption?.ToolName is not null;
}

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

    /// <summary>How many tasks cannot progress until the person acts.</summary>
    public int NeedingAttention => Tasks.Count(task => task.NeedsAttention);
}

/// <summary>Builds the project's unfinished-work list from the run snapshots the client already holds.</summary>
public interface IChatUnfinishedStatisticsCalculator
{
    /// <param name="chats">The project's chats, as the sidebar knows them.</param>
    /// <param name="runs">Every run snapshot the client has, for the whole project.</param>
    ChatUnfinishedStatistics Calculate(IReadOnlyList<ChatSummary> chats, IReadOnlyList<ChatRunSnapshot> runs);

    /// <summary>
    /// The runs the latest start of the Host found cut short, across every project, while that
    /// start is later than <paramref name="seenAt"/> and something of it is still stopped.
    /// </summary>
    /// <param name="chats">Every chat the client knows, of any project; a run whose chat is not
    /// among them is left out until it is.</param>
    /// <param name="runs">Every run snapshot the client has.</param>
    /// <param name="seenAt">The start whose stopped work this device has already dealt with.</param>
    RestartRecovery? FindRestart(IReadOnlyList<ChatSummary> chats, IReadOnlyList<ChatRunSnapshot> runs, DateTimeOffset? seenAt);
}

/// <summary>How long an automatic resume after a restart waits, so the person can still cancel it.</summary>
public static class RestartRecoveryTiming
{
    public const int CountdownSeconds = 10;
}

/// <summary>
/// The work one start of the Host found cut short: what the application's restart stopped. It is
/// shown as its own group, above the rest of the unfinished work, until it is dealt with.
/// </summary>
/// <param name="RecoveredAt">When the Host started again; every run of the group shares it.</param>
/// <param name="StoppedAt">When the application was closed; null when it stopped without closing.</param>
/// <param name="Tasks">The stopped runs, ordered like the rest of the unfinished work.</param>
/// <param name="ProjectNames">Names of the projects the tasks belong to, for rows of other projects.</param>
/// <param name="AutoResumeAt">When the countdown of an automatic resume ends; null without one.</param>
/// <param name="Progress">How far a resume of the whole group has got while it runs.</param>
public sealed record RestartRecovery(
    DateTimeOffset RecoveredAt,
    DateTimeOffset? StoppedAt,
    IReadOnlyList<ChatUnfinishedTask> Tasks,
    IReadOnlyDictionary<Guid, string>? ProjectNames = null,
    DateTimeOffset? AutoResumeAt = null,
    (int Done, int Total)? Progress = null)
{
    /// <summary>Some run was still generating when the Host stopped without closing.</summary>
    public bool Crashed => Tasks.Any(task => task.Interruption?.Cause == RunInterruptionCause.Crash);

    /// <summary>What a resume of the group, by hand or automatic, resumes: a tool call with an unknown outcome waits for the person.</summary>
    public IReadOnlyList<ChatUnfinishedTask> Resumable => Tasks.Where(task => task.CanResume && !task.HasUnknownToolOutcome).ToArray();

    public bool Contains(Guid chatId, Guid branchId) => Tasks.Any(task => task.ChatId == chatId && task.BranchId == branchId);
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
                CanResume(run, described.Reason),
                chat.LastActivityAt,
                DescribeDetail(run, described.Reason),
                described.Reason == ChatUnfinishedReason.Wait ? run.Wait?.RetryAt : null,
                chat.ProjectId,
                described.Reason == ChatUnfinishedReason.Restart ? run.Interruption : null));
        }

        tasks.Sort(Compare);
        var chatsWithWork = tasks.Select(task => task.ChatId).Distinct().Count();
        return new ChatUnfinishedStatistics(tasks, chatsWithWork, tasks.Count(task => task.CanResume));
    }

    public RestartRecovery? FindRestart(IReadOnlyList<ChatSummary> chats, IReadOnlyList<ChatRunSnapshot> runs, DateTimeOffset? seenAt)
    {
        // Each start stamps the runs it recovers with one moment, so the latest one names the group.
        var latest = runs.Where(run => run is { Status: ChatRunStatus.Interrupted, Interruption: not null })
            .Select(run => run.Interruption!.RecoveredAt).DefaultIfEmpty().Max();
        if (latest == default || latest <= seenAt) return null;
        var restarted = runs.Where(run => run is { Status: ChatRunStatus.Interrupted } && run.Interruption?.RecoveredAt == latest).ToArray();
        var tasks = Calculate(chats, restarted).Tasks;
        if (tasks.Count == 0) return null;
        var stoppedAt = restarted.Select(run => run.Interruption!.StoppedAt).Where(at => at is not null).Min();
        return new RestartRecovery(latest, stoppedAt, tasks);
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
        ChatUnfinishedReason.Paused or ChatUnfinishedReason.Interrupted or ChatUnfinishedReason.Restart => 2,
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
        if (run.Status == ChatRunStatus.Interrupted)
            return (run.Interruption is null ? ChatUnfinishedReason.Interrupted : ChatUnfinishedReason.Restart, true);
        if (run.Status == ChatRunStatus.Generating) return (ChatUnfinishedReason.Running, false);
        if (run.Queue.Count > 0) return (ChatUnfinishedReason.Queue, false);
        // A failure with no recovery action left, or a finished run: nothing waits, nothing is offered.
        return null;
    }

    // Resume is offered only where it is the thing the run waits for. An approval or a question needs
    // an answer, a limit lifts on its own and a generating run is already moving: a resume button
    // there would either be refused or do nothing, so those rows open the chat instead.
    private static bool CanResume(ChatRunSnapshot run, ChatUnfinishedReason reason) => reason switch
    {
        ChatUnfinishedReason.Paused or ChatUnfinishedReason.Interrupted or ChatUnfinishedReason.Restart
            or ChatUnfinishedReason.Queue => true,
        ChatUnfinishedReason.Failed => run.RecoveryActions?.Contains(RunRecoveryAction.Resume) == true,
        _ => false
    };

    private const int DetailLength = 120;

    // The words a person needs to decide without opening the chat. Only what the snapshot states is
    // used; a reason the snapshot has nothing more to say about gets no detail.
    private static string? DescribeDetail(ChatRunSnapshot run, ChatUnfinishedReason reason) => reason switch
    {
        ChatUnfinishedReason.Approval when run.PendingApproval is { } approval => approval.BatchSize > 1
            ? $"{approval.Name} ({approval.CallIndex} of {approval.BatchSize})"
            : approval.Name,
        ChatUnfinishedReason.Prompt when run.PendingPrompt is { Questions: [var first, ..] } prompt =>
            Shorten(first.Text) + (prompt.Questions.Count > 1 ? $" (+{prompt.Questions.Count - 1} more)" : null),
        ChatUnfinishedReason.Failed => Shorten(run.Error),
        // The tool comes first: what it did is unknown, and that is what the person has to check.
        ChatUnfinishedReason.Restart when run.Interruption?.ToolName is { } tool => $"Was running {tool}; its outcome is unknown",
        ChatUnfinishedReason.Restart when run.Queue is [var next, ..] => "Was answering: " + Shorten(next.Content),
        // Only a shutdown commits the part already written; a crash loses what was only streamed.
        ChatUnfinishedReason.Restart => run.Interruption?.Cause == RunInterruptionCause.Shutdown
            ? "Was writing the answer; the part written is kept"
            : "Was writing the answer",
        ChatUnfinishedReason.Queue or ChatUnfinishedReason.Paused or ChatUnfinishedReason.Interrupted
            when run.Queue is [var next, ..] => Shorten(next.Content),
        _ => null
    };

    // One line, whitespace collapsed, cut at a word where possible.
    private static string? Shorten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (line.Length <= DetailLength) return line;
        var cut = line.LastIndexOf(' ', DetailLength);
        return line[..(cut > DetailLength / 2 ? cut : DetailLength)] + "…";
    }
}
