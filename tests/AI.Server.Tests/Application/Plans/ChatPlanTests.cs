namespace AI.Application.Tests.Plans;

using System.Text.Json;
using AI.Application.Plans;
using AI.Contracts.Plans;
using AI.Domain.Chats;
using Shouldly;
using Xunit;

public class ChatPlanTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A moment <paramref name="minutes"/> into the plan, with nothing waited for: both clocks agree.</summary>
    private static ChatPlanMoment At(double minutes) => new(Start.AddMinutes(minutes), minutes * 60);

    private static ChatPlanStep Step(string title, int weight, ChatPlanStepStatus status = ChatPlanStepStatus.Pending,
        int progress = 0, double? minutes = null, double? from = null, double? to = null) =>
        new(title, weight, status, progress, Minutes: minutes,
            StartedAt: from is { } a ? Start.AddMinutes(a) : null, FinishedAt: to is { } b ? Start.AddMinutes(b) : null,
            StartedClock: from * 60, FinishedClock: to * 60);

    [Fact]
    public void ShouldMeasureProgressInWeightAndForecastAtThePaceOfTheCompletedSteps()
    {
        var plan = new ChatPlan(null,
        [
            Step("Study", 2, ChatPlanStepStatus.Completed, 100, from: 0, to: 10),
            Step("Build", 4, ChatPlanStepStatus.InProgress, 50, from: 10),
            Step("Verify", 2)
        ], Start.AddMinutes(10), 600);

        var progress = ChatPlanProgress.Of(plan, 1200);

        progress.Fraction.ShouldBe(0.5);
        progress.Percent.ShouldBe(50);
        progress.Completed.ShouldBe(1);
        progress.Total.ShouldBe(3);
        progress.Current.ShouldBe(1);
        progress.IsFinished.ShouldBeFalse();
        progress.Elapsed.ShouldBe(TimeSpan.FromMinutes(20));
        // 300 s per unit of weight: Build is due 20 min and has used 10, Verify 10.
        progress.Remaining.ShouldBe(TimeSpan.FromMinutes(20));
        progress.RemainingLow.ShouldBe(TimeSpan.FromMinutes(12));
        progress.RemainingHigh.ShouldBe(TimeSpan.FromMinutes(28));
        progress.Steps[0].ShouldBe(new ChatPlanStepTiming(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10), false));
        progress.Steps[1].ShouldBe(new ChatPlanStepTiming(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20), false));
        progress.Steps[2].ShouldBe(new ChatPlanStepTiming(null, TimeSpan.FromMinutes(10), false));
    }

    [Fact]
    public void ShouldLetDroppedStepsLeaveTheMeasureSoThePlanCanFinish()
    {
        var plan = new ChatPlan(null,
        [
            Step("Study", 1, ChatPlanStepStatus.Completed, 100, from: 0, to: 5),
            Step("Optional", 5, ChatPlanStepStatus.Skipped),
            Step("Broken", 3, ChatPlanStepStatus.Failed, 30, from: 5, to: 6)
        ], Start.AddMinutes(6), 360);

        var progress = ChatPlanProgress.Of(plan, 3600);

        progress.Percent.ShouldBe(100);
        progress.Completed.ShouldBe(1);
        progress.IsFinished.ShouldBeTrue();
        progress.Current.ShouldBeNull();
        progress.Remaining.ShouldBeNull();
        progress.Elapsed.ShouldBe(TimeSpan.FromMinutes(6));
    }

    [Fact]
    public void ShouldForecastFromTheEstimatesBeforeAnyWorkIsDoneWithAWideRange()
    {
        var plan = new ChatPlan(null,
        [
            Step("Study", 1, ChatPlanStepStatus.InProgress, minutes: 5, from: 0),
            Step("Build", 3, minutes: 15)
        ], Start, 0);

        var progress = ChatPlanProgress.Of(plan, 60);

        progress.Percent.ShouldBe(0);
        progress.Remaining.ShouldBe(TimeSpan.FromMinutes(20));
        progress.RemainingLow.ShouldBe(TimeSpan.FromMinutes(3));
        progress.RemainingHigh.ShouldBe(TimeSpan.FromMinutes(37));
    }

    [Fact]
    public void ShouldCorrectTheEstimatesByHowTheFinishedStepsWentAsFarAsThePlanHasCome()
    {
        var plan = new ChatPlan(null,
        [
            Step("Study", 1, ChatPlanStepStatus.Completed, 100, minutes: 5, from: 0, to: 10),
            Step("Build", 1, minutes: 10)
        ], Start.AddMinutes(10), 600);

        var progress = ChatPlanProgress.Of(plan, 600);

        // Twice the estimate, trusted halfway: one and a half times the next estimate.
        progress.Remaining.ShouldBe(TimeSpan.FromMinutes(15));
        progress.Steps[0].ShouldBe(new ChatPlanStepTiming(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5), true));
    }

    [Fact]
    public void ShouldFlagAStepRunningWellOverItsExpectedTime()
    {
        var plan = new ChatPlan(null, [Step("Build", 1, ChatPlanStepStatus.InProgress, 20, minutes: 2, from: 0)], Start, 0);

        var early = ChatPlanProgress.Of(plan, 150);
        var late = ChatPlanProgress.Of(plan, 600);

        early.Steps[0].IsOver.ShouldBeFalse();
        late.Steps[0].IsOver.ShouldBeTrue();
        // Over its time, what it reports still to do stands.
        late.Remaining.ShouldBe(TimeSpan.FromSeconds(96));
    }

    [Fact]
    public void ShouldStampTimesOnBothClocksWhenStatusesChange()
    {
        var plan = ChatPlanEditor.Replace(null, " Fix ",
            [new("Study", 2, ChatPlanStepStatus.InProgress), new("Build", 20, Minutes: 7)], At(0));

        plan.Title.ShouldBe("Fix");
        plan.Steps[0].StartedAt.ShouldBe(Start);
        plan.Steps[0].StartedClock.ShouldBe(0);
        plan.Steps[1].Weight.ShouldBe(10);
        plan.Steps[1].Minutes.ShouldBe(7);
        plan.Steps[1].StartedAt.ShouldBeNull();

        plan = ChatPlanEditor.Update(plan,
        [
            new(1, ChatPlanStepStatus.Completed),
            new(2, ChatPlanStepStatus.InProgress, 40, "Writing the parser")
        ], At(7));

        plan.Steps[0].ShouldBe(new ChatPlanStep("Study", 2, ChatPlanStepStatus.Completed, 100, null, Start, Start.AddMinutes(7),
            null, 0, 420));
        plan.Steps[1].ShouldBe(new ChatPlanStep("Build", 10, ChatPlanStepStatus.InProgress, 40, "Writing the parser",
            Start.AddMinutes(7), null, 7, 420));
        plan.UpdatedAt.ShouldBe(Start.AddMinutes(7));
        plan.Clock.ShouldBe(420);

        plan = ChatPlanEditor.Update(plan, [new(2, Progress: 120, Minutes: 12)], At(8));
        plan.Steps[1].Progress.ShouldBe(99);
        plan.Steps[1].Minutes.ShouldBe(12);
        plan.Steps[1].StartedClock.ShouldBe(420);
    }

    [Fact]
    public void ShouldLeaveTimeSpentWaitingOutOfAStep()
    {
        var plan = ChatPlanEditor.Replace(null, null, [new("Ask and build", Status: ChatPlanStepStatus.InProgress)], At(0));

        // Ten minutes by the wall clock, five of them waiting for the person.
        plan = ChatPlanEditor.Update(plan, [new(1, ChatPlanStepStatus.Completed)], new ChatPlanMoment(Start.AddMinutes(10), 300));

        plan.Steps[0].Spent(plan, plan.Clock).ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void ShouldPutStepsStampedBeforeTheWorkingClockOnIt()
    {
        var legacy = new ChatPlan(null,
            [new ChatPlanStep("Study", Status: ChatPlanStepStatus.InProgress, StartedAt: Start.AddMinutes(4))],
            Start.AddMinutes(10), 100);

        legacy.Steps[0].Spent(legacy, 100).ShouldBe(TimeSpan.FromMinutes(6));
        var plan = ChatPlanEditor.Update(legacy, [new(1, ChatPlanStepStatus.Completed)],
            new ChatPlanMoment(Start.AddMinutes(11), 160));
        plan.Steps[0].Spent(plan, plan.Clock).ShouldBe(TimeSpan.FromMinutes(7));
    }

    [Fact]
    public void ShouldKeepTheTimesOfStepsARewrittenPlanKeeps()
    {
        var plan = ChatPlanEditor.Replace(null, "Fix", [new("Study", Status: ChatPlanStepStatus.Completed), new("Build")], At(0));

        plan = ChatPlanEditor.Replace(plan, null,
            [new("study", Status: ChatPlanStepStatus.Completed), new("Build", 3, ChatPlanStepStatus.InProgress), new("Docs")],
            At(4));

        plan.Title.ShouldBe("Fix");
        plan.Steps[0].Title.ShouldBe("study");
        // Laid out as already done, it never had a start to measure from.
        plan.Steps[0].StartedAt.ShouldBeNull();
        plan.Steps[0].FinishedAt.ShouldBe(Start);
        plan.Steps[1].StartedAt.ShouldBe(Start.AddMinutes(4));
        plan.Steps[1].StartedClock.ShouldBe(240);
    }

    [Fact]
    public void ShouldTimeAStepFinishedStraightFromWaitingFromThePlansPreviousChange()
    {
        var plan = ChatPlanEditor.Replace(null, null,
            [new("Study", Status: ChatPlanStepStatus.InProgress), new("Find"), new("Build"), new("Docs")], At(0));
        plan = ChatPlanEditor.Update(plan, [new(1, ChatPlanStepStatus.Completed)], At(5));

        plan = ChatPlanEditor.Update(plan,
            [new(2, ChatPlanStepStatus.Completed), new(3, ChatPlanStepStatus.Failed), new(4, ChatPlanStepStatus.Skipped)],
            At(7));

        plan.Steps[1].Spent(plan, plan.Clock).ShouldBe(TimeSpan.FromMinutes(2));
        plan.Steps[2].Spent(plan, plan.Clock).ShouldBe(TimeSpan.FromMinutes(2));
        plan.Steps[3].StartedAt.ShouldBeNull();
        plan.Steps[3].Spent(plan, plan.Clock).ShouldBeNull();
    }

    [Fact]
    public void ShouldRejectStepsOutsideThePlan()
    {
        var plan = ChatPlanEditor.Replace(null, null, [new("Study")], At(0));

        Should.Throw<ArgumentException>(() => ChatPlanEditor.Update(plan, [new(2, ChatPlanStepStatus.Completed)], At(0)))
            .Message.ShouldContain("steps 1–1");
        Should.Throw<ArgumentException>(() => ChatPlanEditor.Replace(null, null, [], At(0)));
        Should.Throw<ArgumentException>(() => ChatPlanEditor.Replace(null, null, [new(" ")], At(0)));
    }

    [Fact]
    public void ShouldClearTheTimesOfAStepPutBackToPending()
    {
        var plan = ChatPlanEditor.Replace(null, null, [new("Study", Status: ChatPlanStepStatus.InProgress, Progress: 30)], At(0));

        plan = ChatPlanEditor.Update(plan, [new(1, ChatPlanStepStatus.Pending)], At(1));

        plan.Steps[0].ShouldBe(new ChatPlanStep("Study"));
    }

    [Fact]
    public void ShouldSettleCriteriaOnlyWithWhatSettledThem()
    {
        var plan = ChatPlanEditor.Replace(null, null, [new("Fix", Status: ChatPlanStepStatus.InProgress)], At(0));
        plan = ChatPlanEditor.Criteria(plan, [new("The parser tests pass"), new("Both themes checked")]);

        ChatPlanCriteriaSummary.Of(plan).ShouldBe(new ChatPlanCriteriaSummary(0, 0, 2, 0));
        Should.Throw<ArgumentException>(() => ChatPlanEditor.Check(plan, [new(1, ChatPlanCriterionStatus.Met)]))
            .Message.ShouldContain("evidence");
        Should.Throw<ArgumentException>(() => ChatPlanEditor.Check(plan, [new(2, ChatPlanCriterionStatus.NotApplicable)]))
            .Message.ShouldContain("why it does not apply");
        Should.Throw<ArgumentException>(() => ChatPlanEditor.Check(plan, [new(3, ChatPlanCriterionStatus.Met, "x")]))
            .Message.ShouldContain("criteria 1–2");

        plan = ChatPlanEditor.Check(plan,
        [
            new(1, ChatPlanCriterionStatus.Met, " 12 of 12  passed ", "call-7"),
            new(2, ChatPlanCriterionStatus.NotApplicable, "No UI changed")
        ]);

        plan.Criteria.ShouldBe(
        [
            new ChatPlanCriterion("The parser tests pass", ChatPlanCriterionStatus.Met, "12 of 12 passed", "call-7"),
            new ChatPlanCriterion("Both themes checked", ChatPlanCriterionStatus.NotApplicable, "No UI changed")
        ]);
        ChatPlanCriteriaSummary.Of(plan).ShouldBe(new ChatPlanCriteriaSummary(1, 0, 0, 1));
        ChatPlanCriteriaSummary.Of(plan).Total.ShouldBe(1);
    }

    [Fact]
    public void ShouldKeepCriteriaThroughACorrectedPlanUntilTheyAreSetAgain()
    {
        var plan = ChatPlanEditor.Replace(null, null, [new("Fix")], At(0));
        plan = ChatPlanEditor.Check(ChatPlanEditor.Criteria(plan, [new("Tests pass")]),
            [new(1, ChatPlanCriterionStatus.Met, "All passed")]);

        var corrected = ChatPlanEditor.Replace(plan, null, [new("Fix"), new("Docs")], At(1));
        corrected.Criteria.ShouldNotBeNull().ShouldHaveSingleItem().Status.ShouldBe(ChatPlanCriterionStatus.Met);

        var reworded = ChatPlanEditor.Criteria(corrected, [new("tests pass"), new("Docs updated")]);
        reworded.Criteria![0].ShouldBe(new ChatPlanCriterion("tests pass", ChatPlanCriterionStatus.Met, "All passed"));
        reworded.Criteria[1].Status.ShouldBe(ChatPlanCriterionStatus.Unverified);

        ChatPlanEditor.Criteria(reworded, []).Criteria.ShouldBeEmpty();
        Should.Throw<ArgumentException>(() => ChatPlanEditor.Criteria(plan,
            Enumerable.Range(1, 9).Select(index => new ChatPlanCriterionInput($"C{index}")).ToArray()));
    }

    [Fact]
    public void ShouldReadTheNewestSuccessfulPlanAlongTheBranch()
    {
        var first = ChatPlanEditor.Replace(null, "First", [new("Study")], At(0));
        var second = ChatPlanEditor.Update(first, [new(1, ChatPlanStepStatus.Completed)], At(1));
        var user = new ChatMessage(new ChatMessageId(Guid.NewGuid()), null, ChatMessageRole.User, "Fix it", Start);
        var call1 = Call(user, "c1");
        var result1 = Result(call1, "c1", Envelope(first));
        var call2 = Call(result1, "c2");
        var result2 = Result(call2, "c2", Envelope(second));
        var call3 = Call(result2, "c3");
        var failed = Result(call3, "c3", JsonSerializer.Serialize(new { isError = true, structuredContent = new { plan = first } }));
        var other = new ChatMessage(new ChatMessageId(Guid.NewGuid()), failed.Id, ChatMessageRole.Assistant, "",
            Start, toolCalls: [new ChatToolCall("c4", "mcp_app__app_read", "{}")]);
        var unrelated = Result(other, "c4", Envelope(first));

        var latest = ChatPlanHistory.Latest([user, call1, result1, call2, result2, call3, failed, other, unrelated]);

        latest.ShouldBe(second, new PlanComparer());
        ChatPlanHistory.Latest([user]).ShouldBeNull();
    }

    [Fact]
    public void ShouldIgnoreResultsThatAreNotPlans()
    {
        ChatPlanResults.TryRead(null).ShouldBeNull();
        ChatPlanResults.TryRead("plain text").ShouldBeNull();
        ChatPlanResults.TryRead("""{"isError":false,"structuredContent":{"plan":null}}""").ShouldBeNull();
        ChatPlanResults.TryRead("""{"isError":false,"structuredContent":{"plan":{"steps":[{"title":"A","status":"Odd"}]}}}""")
            .ShouldBeNull();
        ChatPlanResults.TryRead("""{"isError":false,"structuredContent":{"plan":{"steps":[{"title":"A","status":"InProgress"}],"updatedAt":"2026-10-15T09:00:00Z"}}}""")
            .ShouldNotBeNull().Steps[0].Status.ShouldBe(ChatPlanStepStatus.InProgress);
    }

    private static ChatMessage Call(ChatMessage parent, string id) => new(new ChatMessageId(Guid.NewGuid()), parent.Id,
        ChatMessageRole.Assistant, "", Start, toolCalls: [new ChatToolCall(id, "mcp_app__plan_update", "{}")]);

    private static ChatMessage Result(ChatMessage parent, string id, string content) => new(new ChatMessageId(Guid.NewGuid()), parent.Id,
        ChatMessageRole.Tool, content, Start, toolCallId: id);

    private static string Envelope(ChatPlan plan) => JsonSerializer.Serialize(new
    {
        isError = false,
        structuredContent = new { plan }
    }, Web);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private sealed class PlanComparer : IEqualityComparer<ChatPlan>
    {
        public bool Equals(ChatPlan? x, ChatPlan? y) => x is not null && y is not null && x.Title == y.Title
            && x.UpdatedAt == y.UpdatedAt && x.Steps.SequenceEqual(y.Steps);
        public int GetHashCode(ChatPlan obj) => obj.Title?.GetHashCode() ?? 0;
    }
}
