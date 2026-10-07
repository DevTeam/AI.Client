namespace AI.Application.Tests.Schedules;

using AI.Contracts.Schedules;
using Shouldly;
using Xunit;

public sealed class ScheduleCalendarTests
{
    private readonly ScheduleCalendar _calendar = new();
    private readonly ScheduleDescriptions _descriptions = new();

    private static DateTimeOffset Utc(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void ShouldFireOnTheNamedWeekdaysOnly()
    {
        var weekdays = new ScheduleRecurrence(ScheduleFrequency.Daily, "2030-01-01", "09:00",
            Weekdays: [ScheduleWeekday.Monday, ScheduleWeekday.Tuesday, ScheduleWeekday.Wednesday, ScheduleWeekday.Thursday, ScheduleWeekday.Friday]);

        // 2030-01-04 is a Friday; the next weekday after Friday 09:00 is Monday 2030-01-07.
        _calendar.Upcoming(weekdays, "UTC", Utc(2030, 1, 4, 9), 2)
            .ShouldBe([Utc(2030, 1, 7, 9), Utc(2030, 1, 8, 9)]);
        _descriptions.Describe(weekdays).ShouldBe("Every weekday at 09:00");
    }

    [Fact]
    public void ShouldFireEveryOtherWeekOnTheChosenDaysFromTheStartWeek()
    {
        var recurrence = new ScheduleRecurrence(ScheduleFrequency.Weekly, "2030-01-02", "18:30", Interval: 2,
            Weekdays: [ScheduleWeekday.Monday, ScheduleWeekday.Thursday]);

        // The start week's Monday is before the start date and does not count.
        _calendar.Upcoming(recurrence, "UTC", Utc(2030, 1, 1), 3)
            .ShouldBe([Utc(2030, 1, 3, 18, 30), Utc(2030, 1, 14, 18, 30), Utc(2030, 1, 17, 18, 30)]);
        _descriptions.Describe(recurrence).ShouldBe("Every 2 weeks on Mon, Thu at 18:30");
    }

    [Fact]
    public void ShouldFireOnTheLastDayAndOnACountedWeekdayOfEachMonth()
    {
        var lastDay = new ScheduleRecurrence(ScheduleFrequency.Monthly, "2030-01-15", "12:00", MonthDay: -1);
        _calendar.Upcoming(lastDay, "UTC", Utc(2030, 1, 15), 2).ShouldBe([Utc(2030, 1, 31, 12), Utc(2030, 2, 28, 12)]);

        var lastFriday = new ScheduleRecurrence(ScheduleFrequency.Monthly, "2030-01-01", "08:00",
            MonthWeekday: new ScheduleMonthWeekday(-1, ScheduleWeekday.Friday));
        _calendar.Upcoming(lastFriday, "UTC", Utc(2030, 1, 1), 2).ShouldBe([Utc(2030, 1, 25, 8), Utc(2030, 2, 22, 8)]);
        _descriptions.Describe(lastFriday).ShouldBe("Every month on the last Friday at 08:00");

        var day31 = new ScheduleRecurrence(ScheduleFrequency.Monthly, "2030-01-31", "08:00");
        _calendar.NextAfter(day31, "UTC", Utc(2030, 2, 1)).ShouldBe(Utc(2030, 2, 28, 8));
    }

    [Fact]
    public void ShouldStopAtTheLastDateOrCountAndOnceItHasFired()
    {
        var counted = new ScheduleRecurrence(ScheduleFrequency.Daily, "2030-01-01", "09:00", Count: 2);
        _calendar.Upcoming(counted, "UTC", Utc(2029, 12, 31), 5).Count.ShouldBe(2);
        _calendar.NextAfter(counted, "UTC", Utc(2030, 1, 2, 9)).ShouldBeNull();

        var until = new ScheduleRecurrence(ScheduleFrequency.Daily, "2030-01-01", "09:00", Until: "2030-01-03");
        _calendar.Upcoming(until, "UTC", Utc(2029, 12, 31), 5).Count.ShouldBe(3);
        _descriptions.Describe(until).ShouldBe("Every day at 09:00, until 3 Jan 2030");

        var once = new ScheduleRecurrence(ScheduleFrequency.Once, "2030-01-01", "09:00");
        _calendar.NextAfter(once, "UTC", Utc(2029, 12, 31)).ShouldBe(Utc(2030, 1, 1, 9));
        _calendar.NextAfter(once, "UTC", Utc(2030, 1, 1, 9)).ShouldBeNull();
    }

    [Fact]
    public void ShouldCountHoursWithoutWalkingFromTheStart()
    {
        var hourly = new ScheduleRecurrence(ScheduleFrequency.Hourly, "2020-01-01", "00:30", Interval: 3);

        _calendar.NextAfter(hourly, "UTC", Utc(2030, 1, 1, 1)).ShouldBe(Utc(2030, 1, 1, 3, 30));
        _descriptions.Describe(hourly).ShouldBe("Every 3 hours from 00:30");
    }

    [Fact]
    public void ShouldKeepTheWallClockTimeAcrossADaylightSavingChange()
    {
        var daily = new ScheduleRecurrence(ScheduleFrequency.Daily, "2030-03-30", "09:00");
        var zone = _calendar.FindTimeZone("Europe/Berlin").ShouldNotBeNull();

        var runs = _calendar.Upcoming(daily, zone.Id, Utc(2030, 3, 29), 2);

        // Berlin moves from UTC+1 to UTC+2 on the last Sunday of March 2030 (31 March).
        runs.ShouldBe([new DateTimeOffset(2030, 3, 30, 9, 0, 0, TimeSpan.FromHours(1)),
            new DateTimeOffset(2030, 3, 31, 9, 0, 0, TimeSpan.FromHours(2))]);
    }

    [Theory]
    [InlineData("2030-1-1", "09:00", "'start' must be a date")]
    [InlineData("2030-01-01", "9am", "'time' must be a time of day")]
    public void ShouldExplainAnInvalidRecurrence(string start, string time, string message)
    {
        _calendar.Validate(new ScheduleRecurrence(ScheduleFrequency.Daily, start, time)).ShouldNotBeNull().ShouldContain(message);
        _calendar.Validate(new ScheduleRecurrence(ScheduleFrequency.Daily, "2030-01-01", "09:00", Interval: 2,
            Weekdays: [ScheduleWeekday.Monday])).ShouldNotBeNull().ShouldContain("interval 1");
    }

    [Fact]
    public void ShouldNameTheHostTimeZoneTheWayTheBrowserDoes()
    {
        var id = _calendar.LocalTimeZoneId;

        _calendar.FindTimeZone(id).ShouldNotBeNull();
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana)) id.ShouldBe(iana);
    }

    [Fact]
    public void ShouldStoreWindowsTimeZonesByTheirIanaId()
    {
        _calendar.CanonicalTimeZoneId(" Russian Standard Time ").ShouldBe("Europe/Moscow");
        _calendar.CanonicalTimeZoneId("Europe/Moscow").ShouldBe("Europe/Moscow");
        _calendar.CanonicalTimeZoneId(" Mars/Olympus ").ShouldBe("Mars/Olympus");
    }

    [Fact]
    public void ShouldKeepOneSpellingOfPickedValues()
    {
        SchedulePickers.Normalize("date", " 2030-01-05 ", _calendar).ShouldBe("2030-01-05");
        SchedulePickers.Normalize("time", "07:05", _calendar).ShouldBe("07:05");
        SchedulePickers.Normalize("time", "7:05", _calendar).ShouldBeNull();
        var recurrence = SchedulePickers.Normalize("recurrence",
            "{ \"frequency\": \"Weekly\", \"start\": \"2030-01-07\", \"time\": \"09:00\", \"weekdays\": [\"Monday\"] }", _calendar);
        recurrence.ShouldBe("{\"frequency\":\"Weekly\",\"start\":\"2030-01-07\",\"time\":\"09:00\",\"interval\":1,\"weekdays\":[\"Monday\"]}");
    }

    [Fact]
    public void ShouldNameRunBranchesByWhenTheyStartedAndHowTheyEnded()
    {
        var run = new ScheduleRunRecord(Guid.NewGuid(), Guid.NewGuid(), 12, Utc(2030, 10, 7, 9), Utc(2030, 10, 7, 9), 2,
            ScheduleRunStatus.Running);

        _descriptions.RunTitle(run, new DateTime(2030, 10, 7, 9, 0, 0), 3).ShouldBe("#12 · Mon 7 Oct 09:00 · retry 1/3");
        _descriptions.RunTitle(run with { Manual = true }, new DateTime(2030, 10, 7, 14, 32, 0), 3, ScheduleRunStatus.Succeeded)
            .ShouldBe("#12 · Mon 7 Oct 14:32 · manual · succeeded");
    }
}
