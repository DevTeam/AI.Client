namespace AI.Web.Tests.Widgets;

using AI.Contracts.Chats;
using AI.Web.Widgets;
using Shouldly;
using Xunit;

public class ChatTeamStatisticsCalculatorTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private readonly ChatTeamStatisticsCalculator _calculator = new();

    [Fact]
    public void ShouldCountOnlyMessagesThatCarryASendingBranch()
    {
        var branch = new List<ChatMessageView>
        {
            User(),
            Assistant(),
            FromBranch(BranchA, "status", T0.AddMinutes(1)),
            FromBranch(BranchB, "done", T0.AddMinutes(2))
        };

        var stats = _calculator.Calculate(branch, null, isRunning: false, ChatWidgetScope.Chat);

        stats.Messages.ShouldBe(2);
        stats.Branches.Count.ShouldBe(2);
        stats.Branches.Select(row => row.Messages).ShouldBe([1, 1]);
    }

    [Fact]
    public void ShouldGroupMessagesBySendingBranchAndOrderByCount()
    {
        var branch = new List<ChatMessageView>
        {
            User(),
            FromBranch(BranchA, "status", T0.AddMinutes(1)),
            FromBranch(BranchB, "question", T0.AddMinutes(2)),
            FromBranch(BranchA, "answer", T0.AddMinutes(3))
        };

        var stats = _calculator.Calculate(branch, null, isRunning: false, ChatWidgetScope.Chat);

        stats.Branches[0].BranchId.ShouldBe(BranchA);
        stats.Branches[0].Messages.ShouldBe(2);
        stats.Branches[0].Intents.Select(item => (item.Intent, item.Count)).ShouldBe([("answer", 1), ("status", 1)]);
        stats.Branches[1].BranchId.ShouldBe(BranchB);
    }

    [Fact]
    public void ShouldKeepIntentsInTheProtocolOrderAndReportUnknownOnesLast()
    {
        var branch = new List<ChatMessageView>
        {
            User(),
            FromBranch(BranchA, "later-invented", T0.AddMinutes(1)),
            FromBranch(BranchA, "blocker", T0.AddMinutes(2)),
            FromBranch(BranchA, "question", T0.AddMinutes(3))
        };

        var stats = _calculator.Calculate(branch, null, isRunning: false, ChatWidgetScope.Chat);

        stats.Intents.Select(item => item.Intent).ShouldBe(["question", "blocker", "later-invented"]);
    }

    [Fact]
    public void ShouldCountMessagesWithoutAnIntentUnderNoIntent()
    {
        var branch = new List<ChatMessageView>
        {
            User(),
            FromBranch(BranchA, intent: null, T0.AddMinutes(1)),
            FromBranch(BranchA, "  ", T0.AddMinutes(2))
        };

        var stats = _calculator.Calculate(branch, null, isRunning: false, ChatWidgetScope.Chat);

        stats.Messages.ShouldBe(2);
        stats.Intents.ShouldBeEmpty();
        stats.Branches.ShouldHaveSingleItem().Intents.ShouldBeEmpty();
    }

    [Fact]
    public void ShouldCountAsidesSeparately()
    {
        var branch = new List<ChatMessageView>
        {
            User(),
            FromBranch(BranchA, "status", T0.AddMinutes(1), MessageDelivery.Aside),
            FromBranch(BranchA, "done", T0.AddMinutes(2))
        };

        var stats = _calculator.Calculate(branch, null, isRunning: false, ChatWidgetScope.Chat);

        stats.Branches.ShouldHaveSingleItem().Asides.ShouldBe(1);
    }

    [Fact]
    public void ShouldReportTheLatestMessageOfEachBranch()
    {
        var early = T0.AddMinutes(1);
        var late = T0.AddMinutes(9);

        var branch = new List<ChatMessageView>
        {
            User(),
            FromBranch(BranchA, "status", late),
            FromBranch(BranchA, "answer", early)
        };

        var row = _calculator.Calculate(branch, null, isRunning: false, ChatWidgetScope.Chat)
            .Branches.ShouldHaveSingleItem();

        row.LastAt.ShouldBe(late);
        row.LastMessageId.ShouldBe(branch[1].Id);
    }

    [Fact]
    public void ShouldNameTheBranchFromTheStoredListAndFallBackWhenItIsMissing()
    {
        var branch = new List<ChatMessageView>
        {
            User(),
            FromBranch(BranchA, "status", T0.AddMinutes(1)),
            FromBranch(BranchB, "status", T0.AddMinutes(2))
        };
        var branches = new List<ChatBranchView>
        {
            new(BranchA, null, "Researcher", null, null),
            new(BranchB, null, "  ", null, null)
        };

        var stats = _calculator.Calculate(branch, branches, isRunning: false, ChatWidgetScope.Chat);

        stats.Branches.Single(row => row.BranchId == BranchA).Title.ShouldBe("Researcher");
        stats.Branches.Single(row => row.BranchId == BranchB).Title.ShouldBe("Branch");
    }

    [Fact]
    public void ShouldLimitTheLastTurnScopeToTheLatestTurnAndCountItsTurns()
    {
        var branch = new List<ChatMessageView>
        {
            User(),
            FromBranch(BranchA, "status", T0.AddMinutes(1)),
            User(),
            FromBranch(BranchB, "done", T0.AddMinutes(2))
        };

        var stats = _calculator.Calculate(branch, null, isRunning: false, ChatWidgetScope.LastTurn);

        stats.Turns.ShouldBe(1);
        stats.TurnsWithMessages.ShouldBe(1);
        stats.Branches.ShouldHaveSingleItem().BranchId.ShouldBe(BranchB);
    }

    [Fact]
    public void ShouldSaySoFarOnlyWhileTheLastTurnIsRunning()
    {
        var branch = new List<ChatMessageView> { User(), FromBranch(BranchA, "status", T0) };

        _calculator.Calculate(branch, null, isRunning: true, ChatWidgetScope.LastTurn).IsRunning.ShouldBeTrue();
        _calculator.Calculate(branch, null, isRunning: true, ChatWidgetScope.Chat).IsRunning.ShouldBeFalse();
        _calculator.Calculate(branch, null, isRunning: false, ChatWidgetScope.LastTurn).IsRunning.ShouldBeFalse();
    }

    [Fact]
    public void ShouldReportAnEmptyTeamForABranchNobodyWroteInto()
    {
        var stats = _calculator.Calculate([User(), Assistant()], null, isRunning: false, ChatWidgetScope.Chat);

        stats.Branches.ShouldBeEmpty();
        stats.Messages.ShouldBe(0);
        stats.HasBranches.ShouldBeFalse();
        stats.Turns.ShouldBe(1);
    }

    private static readonly Guid BranchA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BranchB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ChatMessageView User() =>
        new(Guid.NewGuid(), null, "User", "Go", T0);

    private static ChatMessageView Assistant() =>
        new(Guid.NewGuid(), null, "Assistant", "Working on it", T0);

    private static ChatMessageView FromBranch(Guid branchId, string? intent, DateTimeOffset at,
        MessageDelivery delivery = MessageDelivery.Turn) =>
        new(Guid.NewGuid(), null, "Assistant", "Team message", at,
            Delivery: delivery, Sender: new MessageSender(Guid.NewGuid(), branchId, intent));
}
