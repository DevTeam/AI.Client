namespace AI.Web.Tests.Widgets;

using AI.Contracts.Chats;
using AI.Contracts.Schedules;
using Shouldly;
using Xunit;

/// <summary>
/// The shared rule of what the sidebar's Scheduled section lists: which moments make a scheduled
/// chat due, which finished runs stay listed, the window they have to fall in, and the order the
/// list comes back in.
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

    [Fact]
    public void AFinishedRunIsRememberedWhenNothingElseIsPending()
    {
        var finished = Now.AddMinutes(-5);

        ScheduledChats.FinishedAt(Schedule() with { Runs = [Run(finished)] }).ShouldBe(finished);
    }

    [Fact]
    public void ARunStillGoingOrWaitingForAPersonHasNotFinished()
    {
        // A blocked run still has work for the dispatcher, and DueAt speaks for it instead.
        ScheduledChats.FinishedAt(Schedule() with { Runs = [Run(null)] }).ShouldBeNull();
        ScheduledChats.FinishedAt(Schedule() with
        {
            Runs = [Run(null, ScheduleRunStatus.Blocked), Run(Now.AddMinutes(-1), ScheduleRunStatus.Succeeded)]
        }).ShouldBe(Now.AddMinutes(-1));
    }

    [Fact]
    public void AScheduleThatHasNeverRunHasNothingToRemember()
    {
        ScheduledChats.FinishedAt(Schedule()).ShouldBeNull();
    }

    [Fact]
    public void TheGuidesDemoIsNeverRemembered()
    {
        ScheduledChats.FinishedAt(Schedule() with { Demo = true, Runs = [Run(Now.AddMinutes(-5))] }).ShouldBeNull();
    }

    [Fact]
    public void AChatWhoseNextRunIsFartherAwayThanTheWindowStillKeepsItsRow()
    {
        // A weekly schedule just ran: its next occurrence is a week off, outside the window.
        var weekly = Summary(Now.AddDays(7)) with { FinishedAt = Now.AddMinutes(-2) };

        var listed = ScheduledChats.Soon([weekly], Now, TimeSpan.FromHours(24), 10);

        listed.ShouldHaveSingleItem().Chat.Id.ShouldBe(weekly.Chat.Id);
    }

    [Fact]
    public void AChatBothComingDueAndFinishedIsListedOnceAsPending()
    {
        var chat = Summary(Now.AddMinutes(2)) with { FinishedAt = Now.AddMinutes(-2) };

        ScheduledChats.Soon([chat], Now, TimeSpan.FromHours(24), 10).ShouldHaveSingleItem().ShouldBe(chat);
    }

    [Fact]
    public void FinishedChatsFollowWhatIsStillComingAndTheLatestOfThemLeads()
    {
        var soon = Summary(Now.AddMinutes(2));
        var older = Finished(Now.AddMinutes(-30));
        var newer = Finished(Now.AddMinutes(-5));

        var listed = ScheduledChats.Soon([older, newer, soon], Now, TimeSpan.FromHours(24), 10);

        listed.ShouldBe([soon, newer, older]);
    }

    [Fact]
    public void AFinishedChatNeverPushesARunThatIsAboutToStartOutOfTheWindow()
    {
        var soon = Summary(Now.AddMinutes(2));
        var finished = Enumerable.Range(0, ScheduledChats.MaxCount)
            .Select(index => Finished(Now.AddMinutes(-index - 1))).ToList();

        var listed = ScheduledChats.Soon([.. finished, soon], Now, TimeSpan.FromHours(24), 3);

        listed[0].Chat.Id.ShouldBe(soon.Chat.Id);
    }

    [Fact]
    public void AFinishedChatIsForgottenOnceNewerOnesPushItPastTheWindow()
    {
        var oldest = Finished(Now.AddMinutes(-60));
        var middle = Finished(Now.AddMinutes(-30));
        var newest = Finished(Now.AddMinutes(-5));
        // A schedule that has not run yet is not listed at all: there is nothing to remember.
        var pending = Summary(Now.AddMinutes(90));

        var listed = ScheduledChats.Soon([oldest, middle, newest], Now, TimeSpan.FromHours(24), 2);

        listed.ShouldBe([newest, middle]);
        ScheduledChats.Soon([pending with { DueAt = null }], Now, TimeSpan.FromHours(24), 10).ShouldBeEmpty();
    }

    private static ChatSchedule Schedule(DateTimeOffset? next = null, DateTimeOffset? retry = null,
        DateTimeOffset? requested = null, bool paused = false) =>
        new(new ChatScheduleSettings("Nightly build",
                new ScheduleRecurrence(ScheduleFrequency.Daily, "2026-10-01", "03:00"), "Europe/Moscow"),
            paused, NextRunAt: next, RetryAt: retry, RunRequestedAt: requested);

    private static ScheduleRunRecord Run(DateTimeOffset? finishedAt,
        ScheduleRunStatus status = ScheduleRunStatus.Succeeded) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 1, Now.AddHours(-1), Now.AddHours(-1), 1, status, FinishedAt: finishedAt);

    private static ScheduledChatSummary Summary(DateTimeOffset dueAt) =>
        new(new ChatSummary(Guid.NewGuid(), Guid.NewGuid(), "Chat", Now, 1, Now, Kind: ChatSchedule.Kind), dueAt);

    private static ScheduledChatSummary Finished(DateTimeOffset finishedAt) =>
        new(new ChatSummary(Guid.NewGuid(), Guid.NewGuid(), "Chat", Now, 1, Now, Kind: ChatSchedule.Kind), null, finishedAt);
}
