namespace AI.Application.Tests.Plans;

using AI.Application.Chats;
using AI.Application.Plans;
using AI.Application.Projects;
using AI.Application.Tools;
using AI.Contracts.Plans;
using AI.Domain.Chats;
using AI.Domain.Projects;
using Moq;
using Shouldly;
using Xunit;

public class ChatPlansTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
    private readonly ToolRunContext _run = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true);
    private readonly TestClock _clock = new() { UtcNow = Start };
    private readonly Mock<IChatRepository> _chats = new();

    [Fact]
    public async Task ShouldStopTheWorkingClockWhileTheRunWaitsForThePerson()
    {
        _chats.Setup(chats => chats.GetAsync(It.IsAny<ProjectId>(), It.IsAny<ChatId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoredChat?)null);
        var plans = new ChatPlans(_chats.Object, _clock);
        using var scope = await plans.BeginAsync(_run, TestContext.Current.CancellationToken);

        _clock.UtcNow = Start.AddMinutes(2);
        var question = plans.WaitForPerson(_run);
        var approval = plans.WaitForPerson(_run);
        _clock.UtcNow = Start.AddMinutes(5);
        question.Dispose();
        plans.Now(_run).Clock.ShouldBe(120);
        _clock.UtcNow = Start.AddMinutes(7);
        approval.Dispose();
        _clock.UtcNow = Start.AddMinutes(8);

        plans.Now(_run).ShouldBe(new ChatPlanMoment(Start.AddMinutes(8), 180));
    }

    [Fact]
    public async Task ShouldGoOnFromThePlansClockAfterItChanges()
    {
        _chats.Setup(chats => chats.GetAsync(It.IsAny<ProjectId>(), It.IsAny<ChatId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoredChat?)null);
        var plans = new ChatPlans(_chats.Object, _clock);
        using var scope = await plans.BeginAsync(_run, TestContext.Current.CancellationToken);
        _clock.UtcNow = Start.AddMinutes(1);
        var plan = ChatPlanEditor.Replace(null, null, [new("Study")], plans.Now(_run));

        plans.TrySet(_run, null, plan).ShouldBeTrue();
        _clock.UtcNow = Start.AddMinutes(3);

        plans.Now(_run).Clock.ShouldBe(180);
        plans.TrySet(_run, null, plan).ShouldBeFalse();
        scope.Dispose();
        plans.Current(_run).ShouldBeNull();
    }

    [Fact]
    public async Task ShouldKnowTheCallsMadeOnTheBranch()
    {
        _chats.Setup(chats => chats.GetAsync(It.IsAny<ProjectId>(), It.IsAny<ChatId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoredChat?)null);
        var plans = new ChatPlans(_chats.Object, _clock);
        using var scope = await plans.BeginAsync(_run, TestContext.Current.CancellationToken);

        plans.NoteCall(_run, "call-1");

        plans.KnowsCall(_run, "call-1").ShouldBeTrue();
        plans.KnowsCall(_run, "call-2").ShouldBeFalse();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }
}
