namespace AI.Contracts.Schedules;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// The arithmetic of schedules, shared by the Host that runs them and the Web that edits them, so
/// the next run a person is shown is the one the dispatcher will start.
/// </summary>
public interface IScheduleCalendar
{
    /// <summary>The first occurrence strictly after <paramref name="after"/>, or null when none is left.</summary>
    DateTimeOffset? NextAfter(ScheduleRecurrence recurrence, string timeZone, DateTimeOffset after);

    /// <summary>Up to <paramref name="count"/> occurrences strictly after <paramref name="after"/>.</summary>
    IReadOnlyList<DateTimeOffset> Upcoming(ScheduleRecurrence recurrence, string timeZone, DateTimeOffset after, int count);

    /// <summary>What is wrong with the recurrence, in words a model or a person can act on; null when valid.</summary>
    string? Validate(ScheduleRecurrence recurrence);

    /// <summary>What is wrong with the settings; null when valid.</summary>
    string? Validate(ChatScheduleSettings settings);

    /// <summary>The time zone by an IANA or Windows id; null when this machine does not know it.</summary>
    TimeZoneInfo? FindTimeZone(string? id);

    /// <summary>The id to store for the time zone this process runs in.</summary>
    string LocalTimeZoneId { get; }

    /// <summary>A moment in the wall-clock time of <paramref name="timeZone"/>.</summary>
    DateTime ToLocal(DateTimeOffset moment, string timeZone);
}

public sealed class ScheduleCalendar : IScheduleCalendar
{
    public const string DateFormat = "yyyy-MM-dd";
    public const string TimeFormat = "HH:mm";
    private const int MaxSteps = 200_000;

    // IANA where the platform names zones the Windows way, so a schedule set on the host reads the
    // same as one set in the browser ("Europe/Moscow", not "Russian Standard Time").
    public string LocalTimeZoneId => TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana)
        ? iana : TimeZoneInfo.Local.Id;

    public DateTimeOffset? NextAfter(ScheduleRecurrence recurrence, string timeZone, DateTimeOffset after) =>
        Upcoming(recurrence, timeZone, after, 1) is [var next] ? next : null;

    public IReadOnlyList<DateTimeOffset> Upcoming(ScheduleRecurrence recurrence, string timeZone, DateTimeOffset after, int count)
    {
        if (count <= 0 || Validate(recurrence) is not null) return [];
        var zone = FindTimeZone(timeZone) ?? TimeZoneInfo.Local;
        var result = new List<DateTimeOffset>(count);
        DateOnly? last = recurrence.Until is { } until ? ParseDate(until) : null;
        foreach (var (local, index) in Occurrences(recurrence, zone, after))
        {
            if (recurrence.Count is { } total && index >= total) break;
            if (last is { } lastDate && DateOnly.FromDateTime(local) > lastDate) break;
            var moment = ToMoment(local, zone);
            if (moment <= after) continue;
            result.Add(moment);
            if (result.Count == count) break;
        }
        return result;
    }

    /// <summary>
    /// Occurrences in local time with their position from the start, in order. Hourly firings are
    /// counted from the first so a long-running schedule is not walked hour by hour from its start.
    /// </summary>
    private static IEnumerable<(DateTime Time, int Index)> Occurrences(ScheduleRecurrence recurrence, TimeZoneInfo zone, DateTimeOffset after)
    {
        var start = ParseDate(recurrence.Start);
        var time = ParseTime(recurrence.Time);
        var interval = Math.Max(1, recurrence.Interval);
        switch (recurrence.Frequency)
        {
            case ScheduleFrequency.Once:
                yield return (start.ToDateTime(time), 0);
                yield break;
            case ScheduleFrequency.Hourly:
            {
                var first = ToMoment(start.ToDateTime(time), zone);
                var step = TimeSpan.FromHours(interval);
                var skip = after > first ? (int)Math.Min(int.MaxValue - 1, Math.Floor((after - first) / step)) : 0;
                // An hour the clocks repeat maps back to its first pass; the duplicate is then not
                // after the previous firing and is skipped.
                for (var index = skip; index < skip + MaxSteps; index++)
                    yield return (TimeZoneInfo.ConvertTime(first + step * index, zone).DateTime, index);
                yield break;
            }
            case ScheduleFrequency.Daily:
            {
                var days = recurrence.Weekdays is { Count: > 0 } weekdays ? weekdays.ToHashSet() : null;
                var index = 0;
                for (var step = 0; step < MaxSteps; step++)
                {
                    var date = start.AddDays(step * interval);
                    if (days is not null && !days.Contains(Weekday(date))) continue;
                    yield return (date.ToDateTime(time), index++);
                }
                yield break;
            }
            case ScheduleFrequency.Weekly:
            {
                var days = (recurrence.Weekdays is { Count: > 0 } weekdays ? weekdays : [Weekday(start)])
                    .Distinct().OrderBy(day => day).ToArray();
                var monday = start.AddDays(-(int)Weekday(start));
                var index = 0;
                for (var week = 0; week < MaxSteps / 7; week++)
                    foreach (var day in days)
                    {
                        var date = monday.AddDays(week * interval * 7 + (int)day);
                        if (date < start) continue;
                        yield return (date.ToDateTime(time), index++);
                    }
                yield break;
            }
            case ScheduleFrequency.Monthly:
            {
                var index = 0;
                for (var step = 0; step < MaxSteps; step++)
                {
                    var month = new DateOnly(start.Year, start.Month, 1).AddMonths(step * interval);
                    var date = recurrence.MonthWeekday is { } counted
                        ? CountedWeekday(month, counted)
                        : DayOfMonth(month, recurrence.MonthDay ?? start.Day);
                    if (date < start) continue;
                    yield return (date.ToDateTime(time), index++);
                }
                yield break;
            }
            case ScheduleFrequency.Yearly:
            {
                var index = 0;
                for (var step = 0; step < MaxSteps; step++)
                {
                    var month = new DateOnly(start.Year, start.Month, 1).AddYears(step * interval);
                    var date = DayOfMonth(month, recurrence.MonthDay ?? start.Day);
                    if (date < start) continue;
                    yield return (date.ToDateTime(time), index++);
                }
                yield break;
            }
        }
    }

    private static DateOnly DayOfMonth(DateOnly month, int day)
    {
        var length = DateTime.DaysInMonth(month.Year, month.Month);
        return new DateOnly(month.Year, month.Month, day < 0 ? length : Math.Min(day, length));
    }

    private static DateOnly CountedWeekday(DateOnly month, ScheduleMonthWeekday counted)
    {
        if (counted.Ordinal < 0)
        {
            var last = DayOfMonth(month, -1);
            return last.AddDays(-(((int)Weekday(last) - (int)counted.Day + 7) % 7));
        }
        var first = month.AddDays(((int)counted.Day - (int)Weekday(month) + 7) % 7);
        return first.AddDays(7 * (counted.Ordinal - 1));
    }

    private static ScheduleWeekday Weekday(DateOnly date) => (ScheduleWeekday)(((int)date.DayOfWeek + 6) % 7);

    /// <summary>
    /// A wall-clock time the clocks skip (a daylight-saving gap) fires when the clocks resume; one
    /// they repeat fires the first time round.
    /// </summary>
    private static DateTimeOffset ToMoment(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var guard = 0;
        while (zone.IsInvalidTime(local) && guard++ < 8) local = local.AddMinutes(15);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    public DateTime ToLocal(DateTimeOffset moment, string timeZone) =>
        TimeZoneInfo.ConvertTime(moment, FindTimeZone(timeZone) ?? TimeZoneInfo.Local).DateTime;

    public TimeZoneInfo? FindTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        id = id.Trim();
        if (TryFind(id) is { } zone) return zone;
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windows) && TryFind(windows) is { } fromIana) return fromIana;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) && TryFind(iana) is { } fromWindows ? fromWindows : null;
    }

    private static TimeZoneInfo? TryFind(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    public string? Validate(ScheduleRecurrence? recurrence)
    {
        if (recurrence is null) return "A recurrence is required.";
        if (!Enum.IsDefined(recurrence.Frequency)) return "Unknown frequency.";
        if (!TryParseDate(recurrence.Start, out var start)) return $"'start' must be a date in the form {DateFormat}.";
        if (!TryParseTime(recurrence.Time, out _)) return $"'time' must be a time of day in the form {TimeFormat}.";
        if (recurrence.Interval is < 1 or > 999) return "'interval' must be 1–999.";
        if (recurrence.Weekdays is { } weekdays && (weekdays.Any(day => !Enum.IsDefined(day)) || weekdays.Distinct().Count() != weekdays.Count))
            return "'weekdays' must name distinct days.";
        if (recurrence is { Frequency: ScheduleFrequency.Daily, Weekdays.Count: > 0, Interval: > 1 })
            return "A daily schedule limited to some weekdays must have interval 1; use Weekly for other patterns.";
        if (recurrence.MonthDay is { } day && day is not (-1 or >= 1 and <= 31)) return "'monthDay' must be 1–31, or -1 for the last day.";
        if (recurrence.MonthWeekday is { } counted && (counted.Ordinal is not (-1 or >= 1 and <= 4) || !Enum.IsDefined(counted.Day)))
            return "'monthWeekday' needs an ordinal of 1–4 or -1 for the last, and a weekday.";
        if (recurrence.MonthWeekday is not null && recurrence.Frequency != ScheduleFrequency.Monthly)
            return "'monthWeekday' applies to Monthly schedules only.";
        if (recurrence.Until is { } until && (!TryParseDate(until, out var last) || last < start))
            return $"'until' must be a date in the form {DateFormat}, not before 'start'.";
        if (recurrence.Count is < 1 or > 100_000) return "'count' must be 1–100000.";
        return null;
    }

    public string? Validate(ChatScheduleSettings? settings)
    {
        if (settings is null) return "Schedule settings are required.";
        if (string.IsNullOrWhiteSpace(settings.Task)) return "'task' must say what each run does.";
        if (settings.Task.Length > 4000) return "'task' is longer than 4000 characters.";
        if (settings.SuccessCriteria is { Length: > 2000 }) return "'successCriteria' is longer than 2000 characters.";
        if (Validate(settings.Recurrence) is { } recurrence) return recurrence;
        if (FindTimeZone(settings.TimeZone) is null) return $"Unknown time zone '{settings.TimeZone}'. Use an IANA id such as 'Europe/Moscow'.";
        if (settings.Retry is { } retry)
        {
            if (retry.MaxAttempts is < 1 or > 10) return "'retry.maxAttempts' must be 1–10.";
            if (retry.DelayMinutes is < 1 or > 10_080) return "'retry.delayMinutes' must be 1–10080.";
            if (retry.Condition is { Length: > 1000 }) return "'retry.condition' is longer than 1000 characters.";
        }
        if (settings.Retention is { } retention)
            foreach (var rule in new[] { retention.Succeeded, retention.Failed, retention.Blocked })
                if (rule is null || !Enum.IsDefined(rule.Action) || rule.DelayMinutes is < 0 or > 525_600)
                    return "Every retention rule needs an action and a delay of 0–525600 minutes.";
        if (settings.Deletion is { } deletion)
        {
            if (deletion.At is null && deletion.AfterLastRunMinutes is null) return "'deletion' needs 'at' or 'afterLastRunMinutes'.";
            if (deletion.AfterLastRunMinutes is < 0 or > 525_600) return "'deletion.afterLastRunMinutes' must be 0–525600.";
        }
        return null;
    }

    public static bool TryParseDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text?.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    public static bool TryParseTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text?.Trim(), TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static DateOnly ParseDate(string text) => DateOnly.ParseExact(text.Trim(), DateFormat, CultureInfo.InvariantCulture);
    private static TimeOnly ParseTime(string text) => TimeOnly.ParseExact(text.Trim(), TimeFormat, CultureInfo.InvariantCulture);

    /// <summary>The one spelling of a recurrence in tool calls and picker answers.</summary>
    public static readonly JsonSerializerOptions RecurrenceJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(ScheduleRecurrence recurrence) => JsonSerializer.Serialize(recurrence, RecurrenceJson);

    public static bool TryDeserialize(string? text, out ScheduleRecurrence? recurrence)
    {
        recurrence = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            recurrence = JsonSerializer.Deserialize<ScheduleRecurrence>(text, RecurrenceJson);
            return recurrence is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
