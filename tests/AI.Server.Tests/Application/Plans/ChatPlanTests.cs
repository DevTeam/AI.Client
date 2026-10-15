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

    [Fact]
    public void ShouldMeasureProgressInWeightIncludingTheShareOfTheCurrentStep()
    {
        var plan = new ChatPlan(null,
        [
            new("Study", 2, ChatPlanStepStatus.Completed, 100, StartedAt: Start, FinishedAt: Start.AddMinutes(10)),
            new("Build", 4, ChatPlanStepStatus.InProgress, 50, StartedAt: Start.AddMinutes(10)),
            new("Verify", 2)
        ], Start.AddMinutes(10));

        var progress = ChatPlanProgress.Of(plan, Start.AddMinutes(20));

        progress.Fraction.ShouldBe(0.5);
        progress.Percent.ShouldBe(50);
        progress.Completed.ShouldBe(1);
        progress.Total.ShouldBe(3);
        progress.Current.ShouldBe(1);
        progress.IsFinished.ShouldBeFalse();
        progress.Elapsed.ShouldBe(TimeSpan.FromMinutes(20));
        progress.Remaining.ShouldBe(TimeSpan.FromMinutes(20));
    }

    [Fact]
    public void ShouldLetDroppedStepsLeaveTheMeasureSoThePlanCanFinish()
    {
        var plan = new ChatPlan(null,
        [
            new("Study", 1, ChatPlanStepStatus.Completed, 100, StartedAt: Start, FinishedAt: Start.AddMinutes(5)),
            new("Optional", 5, ChatPlanStepStatus.Skipped),
            new("Broken", 3, ChatPlanStepStatus.Failed, 30, StartedAt: Start.AddMinutes(5), FinishedAt: Start.AddMinutes(6))
        ], Start.AddMinutes(6));

        var progress = ChatPlanProgress.Of(plan, Start.AddHours(1));

        progress.Percent.ShouldBe(100);
        progress.Completed.ShouldBe(1);
        progress.IsFinished.ShouldBeTrue();
        progress.Current.ShouldBeNull();
        progress.Remaining.ShouldBeNull();
        progress.Elapsed.ShouldBe(TimeSpan.FromMinutes(6));
    }

    [Fact]
    public void ShouldNotEstimateTheRemainingTimeBeforeAnyWorkIsDone()
    {
        var plan = new ChatPlan(null, [new("Study", 1, ChatPlanStepStatus.InProgress, StartedAt: Start), new("Build")], Start);

        var progress = ChatPlanProgress.Of(plan, Start.AddMinutes(3));

        progress.Percent.ShouldBe(0);
        progress.Current.ShouldBe(0);
        progress.Remaining.ShouldBeNull();
    }

    [Fact]
    public void ShouldStampTimesWhenStatusesChange()
    {
        var plan = ChatPlanEditor.Replace(null, " Fix ", [new("Study", 2, ChatPlanStepStatus.InProgress), new("Build", 20)], Start);

        plan.Title.ShouldBe("Fix");
        plan.Steps[0].StartedAt.ShouldBe(Start);
        plan.Steps[1].Weight.ShouldBe(10);
        plan.Steps[1].StartedAt.ShouldBeNull();

        plan = ChatPlanEditor.Update(plan,
        [
            new(1, ChatPlanStepStatus.Completed),
            new(2, ChatPlanStepStatus.InProgress, 40, "Writing the parser")
        ], Start.AddMinutes(7));

        plan.Steps[0].ShouldBe(new ChatPlanStep("Study", 2, ChatPlanStepStatus.Completed, 100, null, Start, Start.AddMinutes(7)));
        plan.Steps[1].ShouldBe(new ChatPlanStep("Build", 10, ChatPlanStepStatus.InProgress, 40, "Writing the parser",
            Start.AddMinutes(7)));
        plan.UpdatedAt.ShouldBe(Start.AddMinutes(7));

        plan = ChatPlanEditor.Update(plan, [new(2, Progress: 120)], Start.AddMinutes(8));
        plan.Steps[1].Progress.ShouldBe(99);
        plan.Steps[1].StartedAt.ShouldBe(Start.AddMinutes(7));
    }

    [Fact]
    public void ShouldKeepTheTimesOfStepsARewrittenPlanKeeps()
    {
        var plan = ChatPlanEditor.Replace(null, "Fix", [new("Study", Status: ChatPlanStepStatus.Completed), new("Build")], Start);

        plan = ChatPlanEditor.Replace(plan, null,
            [new("study", Status: ChatPlanStepStatus.Completed), new("Build", 3, ChatPlanStepStatus.InProgress), new("Docs")],
            Start.AddMinutes(4));

        plan.Title.ShouldBe("Fix");
        plan.Steps[0].Title.ShouldBe("study");
        // Laid out as already done, it never had a start to measure from.
        plan.Steps[0].StartedAt.ShouldBeNull();
        plan.Steps[0].FinishedAt.ShouldBe(Start);
        plan.Steps[1].StartedAt.ShouldBe(Start.AddMinutes(4));
    }

    [Fact]
    public void ShouldTimeAStepFinishedStraightFromWaitingFromThePlansPreviousChange()
    {
        var plan = ChatPlanEditor.Replace(null, null,
            [new("Study", Status: ChatPlanStepStatus.InProgress), new("Find"), new("Build"), new("Docs")], Start);
        plan = ChatPlanEditor.Update(plan, [new(1, ChatPlanStepStatus.Completed)], Start.AddMinutes(5));

        plan = ChatPlanEditor.Update(plan,
            [new(2, ChatPlanStepStatus.Completed), new(3, ChatPlanStepStatus.Failed), new(4, ChatPlanStepStatus.Skipped)],
            Start.AddMinutes(7));

        plan.Steps[1].Duration(Start.AddMinutes(7)).ShouldBe(TimeSpan.FromMinutes(2));
        plan.Steps[2].Duration(Start.AddMinutes(7)).ShouldBe(TimeSpan.FromMinutes(2));
        plan.Steps[3].StartedAt.ShouldBeNull();
        plan.Steps[3].Duration(Start.AddMinutes(7)).ShouldBeNull();
    }

    [Fact]
    public void ShouldRejectStepsOutsideThePlan()
    {
        var plan = ChatPlanEditor.Replace(null, null, [new("Study")], Start);

        Should.Throw<ArgumentException>(() => ChatPlanEditor.Update(plan, [new(2, ChatPlanStepStatus.Completed)], Start))
            .Message.ShouldContain("steps 1–1");
        Should.Throw<ArgumentException>(() => ChatPlanEditor.Replace(null, null, [], Start));
        Should.Throw<ArgumentException>(() => ChatPlanEditor.Replace(null, null, [new(" ")], Start));
    }

    [Fact]
    public void ShouldClearTheTimesOfAStepPutBackToPending()
    {
        var plan = ChatPlanEditor.Replace(null, null, [new("Study", Status: ChatPlanStepStatus.InProgress, Progress: 30)], Start);

        plan = ChatPlanEditor.Update(plan, [new(1, ChatPlanStepStatus.Pending)], Start.AddMinutes(1));

        plan.Steps[0].ShouldBe(new ChatPlanStep("Study"));
    }

    [Fact]
    public void ShouldReadTheNewestSuccessfulPlanAlongTheBranch()
    {
        var first = ChatPlanEditor.Replace(null, "First", [new("Study")], Start);
        var second = ChatPlanEditor.Update(first, [new(1, ChatPlanStepStatus.Completed)], Start.AddMinutes(1));
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
