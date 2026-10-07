namespace AI.Web.Widgets;

using System.Globalization;
using AI.Contracts.Schedules;

/// <summary>The schedule widget's words and figures, kept out of the component so they can be tested.</summary>
public interface IChatSchedulePresentation
{
    /// <summary>"2h 14m", "38s", "3d 4h": how long until a moment, for the headline countdown.</summary>
    string Countdown(DateTimeOffset at, DateTimeOffset now);

    /// <summary>"Today 09:00", "Tomorrow 09:00", "Thu 9 Oct 09:00" in the schedule's time zone.</summary>
    string Moment(DateTimeOffset at, string timeZone, DateTimeOffset now);

    string Duration(int minutes);

    string Retention(ScheduleRetentionRule rule);

    string Retry(ScheduleRetry? retry);

    string Deletion(ScheduleChatDeletion? deletion, string timeZone, DateTimeOffset now);

    /// <summary>The folded header and status pill: "Paused", "Running #12", "Waits for you", "Finished".</summary>
    ChatScheduleState State(ChatSchedule schedule);

    /// <summary>A starting point for a person setting a schedule up in the widget: every value is shown and editable before it is saved.</summary>
    ChatScheduleSettings Draft(string task, string timeZone, DateTime localNow);
}

public enum ChatScheduleStateKind { Active, Paused, Running, Blocked, Finished }

public sealed record ChatScheduleState(ChatScheduleStateKind Kind, string Label, string Title);

public sealed class ChatSchedulePresentation(IScheduleCalendar calendar, IScheduleDescriptions descriptions) : IChatSchedulePresentation
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public string Countdown(DateTimeOffset at, DateTimeOffset now)
    {
        var left = at - now;
        if (left <= TimeSpan.Zero) return "now";
        if (left < TimeSpan.FromMinutes(1)) return $"{Math.Ceiling(left.TotalSeconds):0}s";
        if (left < TimeSpan.FromHours(1)) return $"{left.Minutes}m {left.Seconds:00}s";
        if (left < TimeSpan.FromDays(1)) return $"{(int)left.TotalHours}h {left.Minutes:00}m";
        return $"{(int)left.TotalDays}d {left.Hours}h";
    }

    public string Moment(DateTimeOffset at, string timeZone, DateTimeOffset now)
    {
        var local = calendar.ToLocal(at, timeZone);
        var today = calendar.ToLocal(now, timeZone).Date;
        var time = local.ToString("HH:mm", Culture);
        if (local.Date == today) return $"Today {time}";
        if (local.Date == today.AddDays(1)) return $"Tomorrow {time}";
        return local.Year == today.Year ? descriptions.Moment(local) : local.ToString("ddd d MMM yyyy HH:mm", Culture);
    }

    public string Duration(int minutes) => minutes switch
    {
        <= 0 => "at once",
        < 60 => $"{minutes} min",
        _ when minutes % 1440 == 0 => minutes == 1440 ? "1 day" : $"{minutes / 1440} days",
        _ when minutes % 60 == 0 => minutes == 60 ? "1 hour" : $"{minutes / 60} hours",
        _ => $"{minutes / 60}h {minutes % 60}m"
    };

    public string Retention(ScheduleRetentionRule rule) => rule.Action == ScheduleRetentionAction.Keep
        ? "Keep"
        : rule.DelayMinutes <= 0 ? "Delete at once" : $"Delete after {Duration(rule.DelayMinutes)}";

    public string Retry(ScheduleRetry? retry) => retry is null
        ? "No retries"
        : $"Up to {retry.MaxAttempts} {(retry.MaxAttempts == 1 ? "retry" : "retries")}, {Duration(retry.DelayMinutes)} apart";

    public string Deletion(ScheduleChatDeletion? deletion, string timeZone, DateTimeOffset now) => deletion switch
    {
        null => "Keep the chat",
        { At: { } at } => $"Delete on {Moment(at, timeZone, now)}",
        { AfterLastRunMinutes: { } minutes } => minutes <= 0 ? "Delete after the last run" : $"Delete {Duration(minutes)} after the last run",
        _ => "Keep the chat"
    };

    public ChatScheduleState State(ChatSchedule schedule) => schedule switch
    {
        { ActiveRun: { Status: ScheduleRunStatus.Blocked } run } =>
            new(ChatScheduleStateKind.Blocked, "Waits for you", $"Run #{run.Number} waits for an approval, an answer or a resume"),
        { ActiveRun: { } run } => new(ChatScheduleStateKind.Running, $"Running #{run.Number}", $"Run #{run.Number} is going now"),
        { Paused: true } => new(ChatScheduleStateKind.Paused, "Paused", "No runs start until the schedule is resumed"),
        { NextRunAt: null, RetryAt: null, RunRequestedAt: null } =>
            new(ChatScheduleStateKind.Finished, "Finished", "The recurrence has no occurrences left"),
        _ => new(ChatScheduleStateKind.Active, "Active", "Runs start on schedule")
    };

    public ChatScheduleSettings Draft(string task, string timeZone, DateTime localNow)
    {
        var tomorrow = DateOnly.FromDateTime(localNow).AddDays(1);
        return new ChatScheduleSettings(task,
            new ScheduleRecurrence(ScheduleFrequency.Daily, tomorrow.ToString(ScheduleCalendar.DateFormat, Culture), "09:00"),
            timeZone,
            Retention: new ScheduleBranchRetention(
                new ScheduleRetentionRule(ScheduleRetentionAction.Delete),
                new ScheduleRetentionRule(ScheduleRetentionAction.Keep),
                new ScheduleRetentionRule(ScheduleRetentionAction.Keep)));
    }
}
