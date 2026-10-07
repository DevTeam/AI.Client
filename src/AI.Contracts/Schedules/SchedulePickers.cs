namespace AI.Contracts.Schedules;

/// <summary>
/// The schedule pickers of <c>ask_user</c>: their kinds and the one spelling of each value, so an
/// answer can be stored as a schedule without anyone guessing what "next Friday at nine" meant.
/// </summary>
public static class SchedulePickers
{
    public const string Date = "date";
    public const string Time = "time";
    public const string Recurrence = "recurrence";

    public static bool IsSchedulePicker(string? kind) => kind is Date or Time or Recurrence;

    /// <summary>
    /// The canonical form of a picked value — <c>yyyy-MM-dd</c>, <c>HH:mm</c> or recurrence JSON — or
    /// null when the value is not one the picker can return.
    /// </summary>
    public static string? Normalize(string? kind, string? value, IScheduleCalendar calendar)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        switch (kind)
        {
            case Date:
                return ScheduleCalendar.TryParseDate(value, out var date) ? date.ToString(ScheduleCalendar.DateFormat, System.Globalization.CultureInfo.InvariantCulture) : null;
            case Time:
                return ScheduleCalendar.TryParseTime(value, out var time) ? time.ToString(ScheduleCalendar.TimeFormat, System.Globalization.CultureInfo.InvariantCulture) : null;
            case Recurrence:
                return ScheduleCalendar.TryDeserialize(value, out var recurrence) && calendar.Validate(recurrence!) is null
                    ? ScheduleCalendar.Serialize(recurrence!) : null;
            default:
                return null;
        }
    }
}
