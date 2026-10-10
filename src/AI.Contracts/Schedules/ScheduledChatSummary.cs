namespace AI.Contracts.Schedules;

using AI.Contracts.Chats;

/// <summary>
/// A scheduled chat the sidebar's Scheduled section lists: the chat as its project's list shows it,
/// and either the moment its next run comes due or, for a chat whose schedule has nothing left to
/// do, the moment its last run finished. What a client lists so a run that is about to start is
/// visible before it starts, and so the run that just finished does not vanish the instant it ends.
/// </summary>
/// <param name="Chat">The chat as its project's chat list shows it.</param>
/// <param name="DueAt">
/// The next occurrence, the pending retry, or the run a person asked for; null for a chat the
/// dispatcher has nothing left to do for. A row without it has no countdown to show.
/// </param>
/// <param name="FinishedAt">When the chat's schedule last finished a run; what orders a chat that has nothing pending.</param>
public sealed record ScheduledChatSummary(ChatSummary Chat, DateTimeOffset? DueAt, DateTimeOffset? FinishedAt = null,
    Guid? BranchId = null, string? BranchTitle = null);

/// <summary>
/// Which scheduled chats the sidebar lists, shared by the Host that answers the question and the
/// clients that ask it, so both mean the same thing by "coming soon" and by "finished".
/// </summary>
public static class ScheduledChats
{
    /// <summary>The window a client looks ahead by: a day, so today's and tonight's runs are in it.</summary>
    public const int DefaultHorizonMinutes = 24 * 60;

    public const int MinHorizonMinutes = 1;
    public const int MaxHorizonMinutes = 7 * 24 * 60;
    public const int DefaultCount = 10;
    public const int MinCount = 1;
    public const int MaxCount = 50;

    /// <summary>
    /// When the dispatcher next acts on the schedule, or null when nothing is pending. A paused
    /// schedule starts no occurrence, but a run a person asked for still goes.
    /// </summary>
    public static DateTimeOffset? DueAt(ChatSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        // The guide's demo only shows a schedule; nothing about it is ever due.
        if (schedule.Demo) return null;
        var moments = new List<DateTimeOffset>();
        if (schedule.RunRequestedAt is { } requested) moments.Add(requested);
        if (schedule.Paused) return moments.Count == 0 ? null : moments.Min();
        if (schedule.RetryAt is { } retry) moments.Add(retry);
        if (schedule.NextRunAt is { } next) moments.Add(next);
        return moments.Count == 0 ? null : moments.Min();
    }

    /// <summary>
    /// When the chat's schedule last finished a run, or null when it has never run one. A run still
    /// going, or waiting for a person, has not finished: the dispatcher still has work for the chat,
    /// which <see cref="DueAt"/> speaks for instead. The guide's demo shows runs without ever having
    /// run one, so nothing about it is remembered either.
    /// </summary>
    public static DateTimeOffset? FinishedAt(ChatSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (schedule.Demo) return null;
        var finishes = (schedule.Runs ?? [])
            .Where(run => run.Status != ScheduleRunStatus.Running && run.FinishedAt is not null)
            .Select(run => run.FinishedAt!.Value)
            .ToList();
        return finishes.Count == 0 ? null : finishes.Max();
    }

    /// <summary>
    /// The chats the sidebar lists: the schedules due within <paramref name="horizon"/> of
    /// <paramref name="now"/>, soonest first, and under them the chats whose schedule has run but is
    /// not coming due within that window — the history of what the section has just watched happen.
    /// A moment that has already passed stays in: the dispatcher has not reached it yet, and a person
    /// watching the sidebar wants to see it arrive. What is still coming leads, so a chat that has
    /// merely finished can never push a run that is about to start out of the window, and a finished
    /// chat is pushed down by the newer ones until the window forgets it — the same way the
    /// Notifications section treats a notification that has been read. A chat whose next run is
    /// further away than the window counts as finished too: a weekly schedule must not lose its row
    /// after every run simply because nothing comes due again today.
    /// </summary>
    public static IReadOnlyList<ScheduledChatSummary> Soon(IEnumerable<ScheduledChatSummary> chats,
        DateTimeOffset now, TimeSpan horizon, int limit)
    {
        ArgumentNullException.ThrowIfNull(chats);
        var until = now + horizon;
        var all = chats.ToList();
        var pending = all.Where(chat => chat.DueAt is { } due && due <= until).OrderBy(chat => chat.DueAt).ToList();
        // Whether a chat came due inside the window decides where it stands; what is left of the list
        // is what the section remembers, so a chat is never in both halves.
        var finished = all.Where(chat => chat.DueAt is not { } due || due > until)
            .Where(chat => chat.FinishedAt is not null)
            .OrderByDescending(chat => chat.FinishedAt);
        return pending.Concat(finished)
            .Take(Math.Clamp(limit, MinCount, MaxCount))
            .ToArray();
    }
}
