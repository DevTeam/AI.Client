namespace AI.Contracts.Schedules;

using AI.Contracts.Chats;

/// <summary>
/// A scheduled chat the dispatcher will act on soon: the chat as its project's list shows it, and
/// the moment it comes due. What a client lists so a run that is about to start is visible before
/// it starts.
/// </summary>
/// <param name="Chat">The chat as its project's chat list shows it.</param>
/// <param name="DueAt">The next occurrence, the pending retry, or the run a person asked for.</param>
public sealed record ScheduledChatSummary(ChatSummary Chat, DateTimeOffset DueAt);

/// <summary>
/// Which scheduled chats count as coming soon, shared by the Host that answers the question and
/// the clients that ask it, so both mean the same thing by "soon".
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
    /// The schedules due within <paramref name="horizon"/> of <paramref name="now"/>, soonest first.
    /// One whose moment has already passed stays in: the dispatcher has not reached it yet, and a
    /// person watching the sidebar wants to see it arrive.
    /// </summary>
    public static IReadOnlyList<ScheduledChatSummary> Soon(IEnumerable<ScheduledChatSummary> chats,
        DateTimeOffset now, TimeSpan horizon, int limit)
    {
        ArgumentNullException.ThrowIfNull(chats);
        var until = now + horizon;
        return chats.Where(chat => chat.DueAt <= until)
            .OrderBy(chat => chat.DueAt)
            .Take(Math.Clamp(limit, MinCount, MaxCount))
            .ToArray();
    }
}
