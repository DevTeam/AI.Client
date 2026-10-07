namespace AI.Contracts.Schedules;

using System.Globalization;

/// <summary>Schedules and their runs in words, the same in the Host, the tools and the Web.</summary>
public interface IScheduleDescriptions
{
    /// <summary>"Every weekday at 09:00", "Once on Thu 8 Oct 2026 at 18:30".</summary>
    string Describe(ScheduleRecurrence recurrence);

    /// <summary>"Tue 7 Oct 09:00": a local moment as branch titles and lists show it.</summary>
    string Moment(DateTime local);

    /// <summary>The title of a run branch: when it fired, and later how it ended.</summary>
    string RunTitle(ScheduleRunRecord run, DateTime localStart, int maxAttempts, ScheduleRunStatus? outcome = null);

    string Outcome(ScheduleRunStatus status);
}

public sealed class ScheduleDescriptions : IScheduleDescriptions
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public string Describe(ScheduleRecurrence recurrence)
    {
        var at = $" at {recurrence.Time}";
        var every = recurrence.Interval > 1;
        var text = recurrence.Frequency switch
        {
            ScheduleFrequency.Once => ScheduleCalendar.TryParseDate(recurrence.Start, out var date)
                ? $"Once on {date.ToString("ddd d MMM yyyy", Culture)}{at}" : $"Once{at}",
            ScheduleFrequency.Hourly => (every ? $"Every {recurrence.Interval} hours" : "Every hour") + $" from {recurrence.Time}",
            ScheduleFrequency.Daily when recurrence.Weekdays is { Count: > 0 } days => $"Every {Days(days)}{at}",
            ScheduleFrequency.Daily => (every ? $"Every {recurrence.Interval} days" : "Every day") + at,
            ScheduleFrequency.Weekly => (every ? $"Every {recurrence.Interval} weeks" : "Every week")
                + (recurrence.Weekdays is { Count: > 0 } weekdays ? $" on {Days(weekdays)}" : StartWeekday(recurrence)) + at,
            ScheduleFrequency.Monthly => (every ? $"Every {recurrence.Interval} months" : "Every month")
                + MonthDay(recurrence) + at,
            ScheduleFrequency.Yearly => (every ? $"Every {recurrence.Interval} years" : "Every year")
                + YearDay(recurrence) + at,
            _ => recurrence.Frequency.ToString()
        };
        if (recurrence.Until is { } until && ScheduleCalendar.TryParseDate(until, out var last))
            text += $", until {last.ToString("d MMM yyyy", Culture)}";
        if (recurrence.Count is { } count && recurrence.Frequency != ScheduleFrequency.Once)
            text += count == 1 ? ", once" : $", {count} times";
        return text;
    }

    public string Moment(DateTime local) => local.ToString("ddd d MMM HH:mm", Culture);

    public string RunTitle(ScheduleRunRecord run, DateTime localStart, int maxAttempts, ScheduleRunStatus? outcome = null)
    {
        var title = $"#{run.Number} · {Moment(localStart)}";
        if (run.Manual) title += " · manual";
        else if (run.Attempt > 1) title += $" · retry {run.Attempt - 1}/{maxAttempts}";
        return outcome is { } status ? $"{title} · {Outcome(status)}" : title;
    }

    public string Outcome(ScheduleRunStatus status) => status switch
    {
        ScheduleRunStatus.Running => "running",
        ScheduleRunStatus.Blocked => "blocked",
        ScheduleRunStatus.Succeeded => "succeeded",
        ScheduleRunStatus.Failed => "failed",
        ScheduleRunStatus.Skipped => "skipped",
        ScheduleRunStatus.Missed => "missed",
        _ => status.ToString().ToLowerInvariant()
    };

    private static string Days(IReadOnlyList<ScheduleWeekday> days)
    {
        var set = days.Distinct().OrderBy(day => day).ToArray();
        if (set.Length == 5 && set.All(day => day <= ScheduleWeekday.Friday)) return "weekday";
        if (set is [ScheduleWeekday.Saturday, ScheduleWeekday.Sunday]) return "weekend day";
        if (set.Length == 7) return "day";
        return string.Join(", ", set.Select(day => day.ToString()[..3]));
    }

    private static string StartWeekday(ScheduleRecurrence recurrence) =>
        ScheduleCalendar.TryParseDate(recurrence.Start, out var start) ? $" on {start.DayOfWeek.ToString()[..3]}" : string.Empty;

    private static string MonthDay(ScheduleRecurrence recurrence)
    {
        if (recurrence.MonthWeekday is { } counted)
            return $" on the {Ordinal(counted.Ordinal)} {counted.Day}";
        var day = recurrence.MonthDay ?? (ScheduleCalendar.TryParseDate(recurrence.Start, out var start) ? start.Day : 1);
        return day < 0 ? " on the last day" : $" on day {day}";
    }

    private static string YearDay(ScheduleRecurrence recurrence)
    {
        if (!ScheduleCalendar.TryParseDate(recurrence.Start, out var start)) return string.Empty;
        var day = recurrence.MonthDay ?? start.Day;
        var month = start.ToString("MMM", Culture);
        return day < 0 ? $" on the last day of {month}" : $" on {day} {month}";
    }

    private static string Ordinal(int ordinal) => ordinal switch
    {
        1 => "first",
        2 => "second",
        3 => "third",
        4 => "fourth",
        _ => "last"
    };
}
