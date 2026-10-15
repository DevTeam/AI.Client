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
    private readonly ChatPlanStatisticsCalculator _calculator = new(new ChatFeed());

    [Fact]
    public void ShouldGiveEveryStepStillInThePlanItsShareAndItsDonePart()
    {
        var plan = new ChatPlan("Fix",
        [
            new("Study", 1, ChatPlanStepStatus.Completed, 100, StartedAt: Start, FinishedAt: Start.AddMinutes(4)),
            new("Build", 2, ChatPlanStepStatus.InProgress, 25, StartedAt: Start.AddMinutes(4)),
            new("Docs", 2, ChatPlanStepStatus.Skipped),
            new("Ship", 1)
        ], Start.AddMinutes(5));

        var stats = _calculator.Calculate([Message("User"), Message("Tool", plan)], Start.AddMinutes(6));

        stats.HasPlan.ShouldBeTrue();
        stats.InLastTurn.ShouldBeTrue();
        stats.Steps.Select(entry => (entry.Number, entry.Share, entry.Done))
            .ShouldBe([(1, .25, 1d), (2, .5, .25), (3, 0d, 0d), (4, .25, 0d)]);
        stats.Progress.ShouldNotBeNull().Percent.ShouldBe(37);
        stats.Progress.Elapsed.ShouldBe(TimeSpan.FromMinutes(6));
    }

    [Fact]
    public void ShouldBeEmptyWithoutAPlan() =>
        _calculator.Calculate([Message("User"), Message("Assistant")], Start).ShouldBe(ChatPlanStatistics.Empty);

    private static ChatMessageView Message(string role, ChatPlan? plan = null) =>
        new(Guid.NewGuid(), null, role, "x", Start, Plan: plan);
}
