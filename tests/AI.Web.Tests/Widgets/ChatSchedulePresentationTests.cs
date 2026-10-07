namespace AI.Web.Tests.Widgets;

using AI.Contracts.Schedules;
using AI.Web.Widgets;
using Shouldly;
using Xunit;

public sealed class ChatSchedulePresentationTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 7, 8, 0, 0, TimeSpan.Zero);
    private readonly ChatSchedulePresentation _presentation = new(new ScheduleCalendar(), new ScheduleDescriptions());

    [Theory]
    [InlineData(38, "38s")]
    [InlineData(14 * 60 + 5, "14m 05s")]
    [InlineData(2 * 3600 + 14 * 60, "2h 14m")]
    [InlineData(3 * 86400 + 4 * 3600, "3d 4h")]
    [InlineData(-5, "now")]
    public void ShouldCountDownInTheLargestUnitsThatMatter(int seconds, string expected) =>
        _presentation.Countdown(Now.AddSeconds(seconds), Now).ShouldBe(expected);

    [Fact]
    public void ShouldNameNearbyDaysAndOtherwiseTheDate()
    {
        _presentation.Moment(Now.AddHours(1), "UTC", Now).ShouldBe("Today 09:00");
        _presentation.Moment(Now.AddDays(1), "UTC", Now).ShouldBe("Tomorrow 08:00");
        _presentation.Moment(Now.AddDays(3), "UTC", Now).ShouldBe("Thu 10 Jan 08:00");
    }

    [Fact]
    public void ShouldSayRulesInWords()
    {
        _presentation.Retention(new ScheduleRetentionRule(ScheduleRetentionAction.Keep)).ShouldBe("Keep");
        _presentation.Retention(new ScheduleRetentionRule(ScheduleRetentionAction.Delete)).ShouldBe("Delete at once");
        _presentation.Retention(new ScheduleRetentionRule(ScheduleRetentionAction.Delete, 1440)).ShouldBe("Delete after 1 day");
        _presentation.Retry(null).ShouldBe("No retries");
        _presentation.Retry(new ScheduleRetry(1, 60)).ShouldBe("Up to 1 retry, 1 hour apart");
        _presentation.Deletion(null, "UTC", Now).ShouldBe("Keep the chat");
        _presentation.Deletion(new ScheduleChatDeletion(AfterLastRunMinutes: 10080), "UTC", Now).ShouldBe("Delete 7 days after the last run");
    }

    [Fact]
    public void ShouldTellRunningAndWaitingApartFromPausedAndFinished()
    {
        var settings = new ChatScheduleSettings("Task", new ScheduleRecurrence(ScheduleFrequency.Once, "2030-01-07", "09:00"), "UTC");
        var running = new ScheduleRunRecord(Guid.NewGuid(), Guid.NewGuid(), 4, Now, Now, 1, ScheduleRunStatus.Running);

        _presentation.State(new ChatSchedule(settings, NextRunAt: Now)).Kind.ShouldBe(ChatScheduleStateKind.Active);
        _presentation.State(new ChatSchedule(settings, Paused: true, NextRunAt: Now)).Label.ShouldBe("Paused");
        _presentation.State(new ChatSchedule(settings, Runs: [running])).Label.ShouldBe("Running #4");
        _presentation.State(new ChatSchedule(settings, Runs: [running with { Status = ScheduleRunStatus.Blocked }])).Label.ShouldBe("Waits for you");
        _presentation.State(new ChatSchedule(settings)).Kind.ShouldBe(ChatScheduleStateKind.Finished);
    }
}
