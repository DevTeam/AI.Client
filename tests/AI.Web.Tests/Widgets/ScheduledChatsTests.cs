namespace AI.Web.Tests.Widgets;

using AI.Contracts.Chats;
using AI.Contracts.Schedules;
using Shouldly;
using Xunit;

/// <summary>
/// The shared rule of what counts as coming soon: which moments make a scheduled chat due, the
/// window it has to fall in, and the order the list comes back in.
/// </summary>
public sealed class ScheduledChatsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NextOccurrenceMakesAScheduleDue()
    {
        var schedule = Schedule(next: Now.AddHours(3));

        ScheduledChats.DueAt(schedule).ShouldBe(Now.AddHours(3));
    }

    [Fact]
    public void TheEarliestOfOccurrenceRetryAndRequestedRunDecides()
    {
        var schedule = Schedule(next: Now.AddHours(3), retry: Now.AddMinutes(20), requested: Now.AddMinutes(5));

        ScheduledChats.DueAt(schedule).ShouldBe(Now.AddMinutes(5));
    }

    [Fact]
    public void APausedScheduleStartsNoOccurrenceButStillHonoursARequestedRun()
    {
        ScheduledChats.DueAt(Schedule(next: Now.AddHours(3), retry: Now.AddMinutes(5), paused: true)).ShouldBeNull();
        ScheduledChats.DueAt(Schedule(next: Now.AddHours(3), requested: Now.AddMinutes(5), paused: true))
            .ShouldBe(Now.AddMinutes(5));
    }

    [Fact]
    public void AScheduleWithNothingPendingIsNeverDue()
    {
        ScheduledChats.DueAt(Schedule()).ShouldBeNull();
    }

    [Fact]
    public void TheGuidesDemoIsNeverDue()
    {
        ScheduledChats.DueAt(Schedule(next: Now.AddMinutes(1)) with { Demo = true }).ShouldBeNull();
    }

    [Fact]
    public void OnlyWhatFallsInsideTheWindowIsSoonAndTheSoonestComesFirst()
    {
        var late = Summary(Now.AddHours(30));
        var soonest = Summary(Now.AddMinutes(2));
        var middle = Summary(Now.AddHours(9));
        var overdue = Summary(Now.AddHours(-1));

        var soon = ScheduledChats.Soon([late, middle, soonest, overdue], Now, TimeSpan.FromHours(24), 10);

        soon.ShouldBe([overdue, soonest, middle]);
    }

    [Fact]
    public void TheLimitIsClampedToWhatTheHostAnswersWith()
    {
        var chats = Enumerable.Range(0, ScheduledChats.MaxCount + 5)
            .Select(index => Summary(Now.AddMinutes(index + 1))).ToArray();

        ScheduledChats.Soon(chats, Now, TimeSpan.FromHours(24), 0).Count.ShouldBe(ScheduledChats.MinCount);
        ScheduledChats.Soon(chats, Now, TimeSpan.FromHours(24), 3).Count.ShouldBe(3);
        ScheduledChats.Soon(chats, Now, TimeSpan.FromHours(24), 500).Count.ShouldBe(ScheduledChats.MaxCount);
    }

    private static ChatSchedule Schedule(DateTimeOffset? next = null, DateTimeOffset? retry = null,
        DateTimeOffset? requested = null, bool paused = false) =>
        new(new ChatScheduleSettings("Nightly build",
                new ScheduleRecurrence(ScheduleFrequency.Daily, "2026-10-01", "03:00"), "Europe/Moscow"),
            paused, NextRunAt: next, RetryAt: retry, RunRequestedAt: requested);

    private static ScheduledChatSummary Summary(DateTimeOffset dueAt) =>
        new(new ChatSummary(Guid.NewGuid(), Guid.NewGuid(), "Chat", Now, 1, Now, Kind: ChatSchedule.Kind), dueAt);
}
