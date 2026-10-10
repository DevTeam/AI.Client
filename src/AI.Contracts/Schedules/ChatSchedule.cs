namespace AI.Contracts.Schedules;

using System.Text.Json.Serialization;

/// <summary>How often a schedule fires. New values go at the end: the names are stored in chat state.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScheduleFrequency>))]
public enum ScheduleFrequency { Once, Hourly, Daily, Weekly, Monthly, Yearly }

[JsonConverter(typeof(JsonStringEnumConverter<ScheduleWeekday>))]
public enum ScheduleWeekday { Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday }

/// <summary>"The first Monday", "the last Friday": a weekday counted within a month.</summary>
/// <param name="Ordinal">1–4 counts from the start of the month; -1 is the last one.</param>
public sealed record ScheduleMonthWeekday(int Ordinal, ScheduleWeekday Day);

/// <summary>
/// When a schedule fires, in the wall-clock time of the schedule's time zone. Dates are
/// <c>yyyy-MM-dd</c> and times <c>HH:mm</c>, so a value reads the same in storage, in a tool call
/// and in an answer to a picker, and never depends on the culture that wrote it.
/// </summary>
/// <param name="Start">The first date the schedule may fire; for <see cref="ScheduleFrequency.Once"/> the date it fires.</param>
/// <param name="Time">The time of day it fires; for <see cref="ScheduleFrequency.Hourly"/> the first firing.</param>
/// <param name="Interval">Every N hours, days, weeks, months or years; ignored by Once.</param>
/// <param name="Weekdays">Weekly: the days it fires. Daily: limits firing to these days (interval 1 only).</param>
/// <param name="MonthDay">Monthly and Yearly: day of the month, 1–31, or -1 for the last day. A month too short fires on its last day.</param>
/// <param name="MonthWeekday">Monthly: fires on a counted weekday instead of a day number.</param>
/// <param name="Until">The last date it may fire, inclusive.</param>
/// <param name="Count">How many times it fires in total.</param>
public sealed record ScheduleRecurrence(
    ScheduleFrequency Frequency,
    string Start,
    string Time,
    int Interval = 1,
    IReadOnlyList<ScheduleWeekday>? Weekdays = null,
    int? MonthDay = null,
    ScheduleMonthWeekday? MonthWeekday = null,
    string? Until = null,
    int? Count = null);

/// <summary>What happens when a run did not succeed.</summary>
/// <param name="MaxAttempts">Retries after the first attempt, 1–10.</param>
/// <param name="DelayMinutes">Wait before each retry, 1–10080.</param>
/// <param name="Condition">When a failure is worth retrying, in the user's words; the run judges it.</param>
public sealed record ScheduleRetry(int MaxAttempts, int DelayMinutes, string? Condition = null);

[JsonConverter(typeof(JsonStringEnumConverter<ScheduleRetentionAction>))]
public enum ScheduleRetentionAction { Keep, Delete }

/// <summary>What the dispatcher does with a run branch that ended one way.</summary>
/// <param name="DelayMinutes">For Delete: how long the branch stays first; 0 deletes it at the dispatcher's next pass.</param>
public sealed record ScheduleRetentionRule(ScheduleRetentionAction Action, int DelayMinutes = 0);

/// <summary>
/// One rule per outcome. A blocked branch waits for a person (an approval or a question); deleting
/// it after a delay stops the run.
/// </summary>
public sealed record ScheduleBranchRetention(
    ScheduleRetentionRule Succeeded,
    ScheduleRetentionRule Failed,
    ScheduleRetentionRule Blocked);

/// <summary>When the scheduled chat itself is deleted. Absent means the chat stays.</summary>
/// <param name="At">An absolute moment.</param>
/// <param name="AfterLastRunMinutes">Minutes after the schedule has nothing left to run and its last run ended.</param>
public sealed record ScheduleChatDeletion(DateTimeOffset? At = null, int? AfterLastRunMinutes = null);

/// <summary>Everything a person or a skill decides about a schedule.</summary>
/// <param name="Task">What a run does, stated so a run branch can carry it out on its own.</param>
/// <param name="TimeZone">The time zone the recurrence is in: an IANA or Windows id.</param>
/// <param name="SuccessCriteria">How a run tells success from failure.</param>
/// <param name="Retry">Absent means a failed run is not retried.</param>
public sealed record ChatScheduleSettings(
    string Task,
    ScheduleRecurrence Recurrence,
    string TimeZone,
    string? SuccessCriteria = null,
    ScheduleRetry? Retry = null,
    ScheduleBranchRetention? Retention = null,
    ScheduleChatDeletion? Deletion = null);

[JsonConverter(typeof(JsonStringEnumConverter<ScheduleRunStatus>))]
public enum ScheduleRunStatus { Running, Blocked, Succeeded, Failed, Skipped, Missed }

/// <summary>One firing of the schedule and what became of it.</summary>
/// <param name="BranchId">The run branch; null for an occurrence that did not run.</param>
/// <param name="Number">The run's number in this chat, shown in the branch title.</param>
/// <param name="ScheduledFor">The occurrence it belongs to; retries share it.</param>
/// <param name="Attempt">1 for the first attempt, then 2, 3… for retries.</param>
/// <param name="Reported">The outcome the run reported itself, before its turn ended.</param>
/// <param name="RetryRequested">False when the run judged the failure not worth retrying.</param>
public sealed record ScheduleRunRecord(
    Guid Id,
    Guid? BranchId,
    int Number,
    DateTimeOffset ScheduledFor,
    DateTimeOffset StartedAt,
    int Attempt,
    ScheduleRunStatus Status,
    bool Manual = false,
    DateTimeOffset? FinishedAt = null,
    DateTimeOffset? BlockedAt = null,
    string? Summary = null,
    ScheduleRunStatus? Reported = null,
    bool? RetryRequested = null,
    DateTimeOffset? BranchDeleteAt = null,
    bool BranchDeleted = false);

/// <summary>
/// The state of a scheduled chat (kind <c>scheduled</c>, state version 1): the settings, whether it
/// is paused, and what the dispatcher keeps between passes.
/// </summary>
/// <param name="Revision">Grows with every change of the settings or the pause; a write may name the one it read.</param>
/// <param name="NextRunAt">The next occurrence, or null when the recurrence has none left.</param>
/// <param name="Occurrences">Occurrences started so far, against <see cref="ScheduleRecurrence.Count"/>.</param>
/// <param name="RetryAt">When the pending retry of the last failed run starts.</param>
/// <param name="RunRequestedAt">A person or a skill asked for a run now.</param>
/// <param name="Demo">
/// Set up by the application guide to show a schedule: the dispatcher starts nothing for it, and the
/// first change a person makes turns it into an ordinary schedule.
/// </param>
public sealed record ChatSchedule(
    ChatScheduleSettings Settings,
    bool Paused = false,
    long Revision = 1,
    DateTimeOffset? NextRunAt = null,
    int Occurrences = 0,
    int RunNumber = 0,
    DateTimeOffset? RetryAt = null,
    DateTimeOffset? RunRequestedAt = null,
    IReadOnlyList<ScheduleRunRecord>? Runs = null,
    bool Demo = false)
{
    public const string Kind = "scheduled";
    public const int StateVersion = 1;

    /// <summary>The run still going, if any: only one runs at a time.</summary>
    [JsonIgnore]
    public ScheduleRunRecord? ActiveRun => Runs?.LastOrDefault(run => run.Status is ScheduleRunStatus.Running or ScheduleRunStatus.Blocked);
}

/// <summary>The schedule of a chat as the API returns it, with the chat's identity.</summary>
/// <param name="Description">The recurrence in words, such as "Every weekday at 09:00".</param>
/// <param name="Now">The host's clock when this was read, so a model can resolve "tomorrow" exactly.</param>
/// <param name="LocalNow">The same moment in the host's time zone, "yyyy-MM-dd HH:mm dddd".</param>
/// <param name="Upcoming">The next occurrences, at most three; empty while paused.</param>
public sealed record ChatScheduleView(Guid ProjectId, Guid ChatId, string ChatKind, ChatSchedule? Schedule, string? Description,
    DateTimeOffset Now = default, string? LocalNow = null, string? HostTimeZone = null,
    IReadOnlyList<DateTimeOffset>? Upcoming = null, Guid? BranchId = null);

/// <param name="Revision">The schedule revision the caller read; null skips the check.</param>
public sealed record SetChatScheduleRequest(ChatScheduleSettings Settings, long? Revision = null, bool? Paused = null);
