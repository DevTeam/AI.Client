namespace AI.Web.Tests.Widgets;

using AI.Contracts.Chats;
using AI.Contracts.Plans;
using AI.Web.Components;
using AI.Web.Widgets;
using Shouldly;
using Xunit;

public sealed class ChatPlanStatisticsCalculatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
    private readonly TestTime _time = new() { Now = Start.AddMinutes(6) };

    [Fact]
    public void ShouldGiveEveryStepStillInThePlanItsShareItsDonePartAndItsTime()
    {
        var plan = new ChatPlan("Fix",
        [
            new("Study", 1, ChatPlanStepStatus.Completed, 100, StartedAt: Start, FinishedAt: Start.AddMinutes(4),
                StartedClock: 0, FinishedClock: 240),
            new("Build", 2, ChatPlanStepStatus.InProgress, 25, StartedAt: Start.AddMinutes(4), Minutes: 4, StartedClock: 240),
            new("Docs", 2, ChatPlanStepStatus.Skipped),
            new("Ship", 1)
        ], Start.AddMinutes(5), 300);

        var stats = Calculator().Calculate([Message("User", Start), Message("Tool", Start, plan)], working: true, waiting: false);

        stats.HasPlan.ShouldBeTrue();
        stats.InLastTurn.ShouldBeTrue();
        stats.Steps.Select(entry => (entry.Number, entry.Share, entry.Done))
            .ShouldBe([(1, .25, 1d), (2, .5, .25), (3, 0d, 0d), (4, .25, 0d)]);
        stats.Progress.ShouldNotBeNull().Percent.ShouldBe(37);
        // A minute past the last update, on top of the five the plan's clock had.
        stats.Progress.Elapsed.ShouldBe(TimeSpan.FromMinutes(6));
        stats.Steps[1].Timing.Spent.ShouldBe(TimeSpan.FromMinutes(2));
        stats.Steps[1].Timing.Expected.ShouldNotBeNull();
    }

    [Fact]
    public void ShouldHoldTheClockOfAPlanNoRunIsWorkingOn()
    {
        var plan = new ChatPlan(null, [new("Study", Status: ChatPlanStepStatus.InProgress, StartedAt: Start, StartedClock: 0)],
            Start.AddMinutes(1), 60);

        var stats = Calculator().Calculate([Message("User", Start), Message("Tool", Start, plan)], working: false, waiting: false);

        stats.Progress.ShouldNotBeNull().Elapsed.ShouldBe(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ShouldBeEmptyWithoutAPlan() =>
        Calculator().Calculate([Message("User", Start), Message("Assistant", Start)], false, false).ShouldBe(ChatPlanStatistics.Empty);

    [Fact]
    public void ShouldStopTheClockWhileTheRunWaitsForThePersonAndGoOnFromTheTurnForAnOldPlan()
    {
        var clock = new ChatPlanClock(_time);
        var plan = new ChatPlan(null, [new("Study", Status: ChatPlanStepStatus.InProgress, StartedClock: 0)], Start, 0);

        // The plan was left at the start; this turn began four minutes later.
        _time.Now = Start.AddMinutes(5);
        clock.Now(plan, Start.AddMinutes(4), waiting: false).ShouldBe(60);
        clock.Now(plan, Start.AddMinutes(4), waiting: true).ShouldBe(60);
        _time.Now = Start.AddMinutes(8);
        clock.Now(plan, Start.AddMinutes(4), waiting: true).ShouldBe(60);
        clock.Now(plan, Start.AddMinutes(4), waiting: false).ShouldBe(60);
        _time.Now = Start.AddMinutes(9);
        clock.Now(plan, Start.AddMinutes(4), waiting: false).ShouldBe(120);

        // The next update carries the Host's own reading; the waits seen before it are done with.
        var updated = plan with { UpdatedAt = Start.AddMinutes(9), Clock = 120 };
        _time.Now = Start.AddMinutes(10);
        clock.Now(updated, Start.AddMinutes(4), waiting: false).ShouldBe(180);
        clock.Now(updated, null, waiting: false).ShouldBe(120);
    }

    private ChatPlanStatisticsCalculator Calculator() => new(new ChatFeed(), new ChatPlanClock(_time));

    private static ChatMessageView Message(string role, DateTimeOffset at, ChatPlan? plan = null) =>
        new(Guid.NewGuid(), null, role, "x", at, Plan: plan);

    private sealed class TestTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
