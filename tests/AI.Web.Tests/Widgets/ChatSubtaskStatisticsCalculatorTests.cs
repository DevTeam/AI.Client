namespace AI.Web.Tests.Widgets;

using AI.Contracts.Chats;
using AI.Contracts.Usage;
using AI.Web.Widgets;
using Shouldly;
using Xunit;

public class ChatSubtaskStatisticsCalculatorTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private readonly ChatSubtaskStatisticsCalculator _calculator = new();

    [Fact]
    public void ShouldLeaveEmptyWhenThereIsNothingToRead()
    {
        var stats = _calculator.Calculate(null, null, [User(), Assistant()], isRunning: false, ChatWidgetScope.Chat);

        stats.Requests.ShouldBe(0);
        stats.HasData.ShouldBeFalse();
        stats.Share.ShouldBeNull();
        stats.TurnsWithSubtasks.ShouldBe(0);
    }

    [Fact]
    public void ShouldReadSubtaskSliceFromWholeChat()
    {
        var chat = ChatUsage(Totals(Requests: 4, InputTokens: 1_000, OutputTokens: 400), SubtaskSlice(Requests: 2, InputTokens: 600, OutputTokens: 200));

        var stats = _calculator.Calculate(chat, null, [User(), Assistant()], isRunning: false, ChatWidgetScope.Chat);

        stats.Requests.ShouldBe(2);
        stats.InputTokens.ShouldBe(600);
        stats.OutputTokens.ShouldBe(200);
        // 800 of 1400 = 0.5714…
        stats.Share.ShouldNotBeNull();
        stats.Share!.Value.ShouldBeInRange(0.57, 0.58);
        stats.HasData.ShouldBeTrue();
        stats.Turns.ShouldBe(1);
        stats.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public void ShouldTakeLastTurnOnlyWhenAsked()
    {
        var storedTurn = TurnUsage(Totals(Requests: 1, InputTokens: 100, OutputTokens: 50), SubtaskSlice(Requests: 0, InputTokens: 0, OutputTokens: 0));
        var liveTurn = TurnUsage(Totals(Requests: 2, InputTokens: 200, OutputTokens: 80), SubtaskSlice(Requests: 1, InputTokens: 120, OutputTokens: 60));
        var chat = ChatUsage(Totals(Requests: 3, InputTokens: 300, OutputTokens: 130), SubtaskSlice(Requests: 1, InputTokens: 120, OutputTokens: 60),
            turns: [storedTurn]);

        var stats = _calculator.Calculate(chat, liveTurn, [User(), Assistant(), User(), Assistant()], isRunning: true,
            ChatWidgetScope.LastTurn);

        stats.Turns.ShouldBe(1);
        stats.Requests.ShouldBe(1);
        stats.InputTokens.ShouldBe(120);
        stats.OutputTokens.ShouldBe(60);
        stats.IsRunning.ShouldBeTrue();
    }

    [Fact]
    public void ShouldPreferLiveTurnWhenItHasMoreRequests()
    {
        var turnId = Guid.NewGuid();
        var stored = TurnUsage(Totals(Requests: 1, InputTokens: 100, OutputTokens: 50), SubtaskSlice(Requests: 0, InputTokens: 0, OutputTokens: 0),
            turnId: turnId);
        var live = TurnUsage(Totals(Requests: 3, InputTokens: 400, OutputTokens: 200), SubtaskSlice(Requests: 2, InputTokens: 300, OutputTokens: 150),
            turnId: turnId);
        var chat = ChatUsage(Totals(Requests: 4, InputTokens: 500, OutputTokens: 250), SubtaskSlice(Requests: 2, InputTokens: 300, OutputTokens: 150),
            turns: [stored]);

        var stats = _calculator.Calculate(chat, live, [User(), Assistant()], isRunning: true, ChatWidgetScope.LastTurn);

        stats.Requests.ShouldBe(2);
        stats.InputTokens.ShouldBe(300);
        stats.OutputTokens.ShouldBe(150);
        stats.IsRunning.ShouldBeTrue();
    }

    [Fact]
    public void ShouldKeepStoredTurnWhenLiveIsNotAhead()
    {
        var turnId = Guid.NewGuid();
        var stored = TurnUsage(Totals(Requests: 2, InputTokens: 200, OutputTokens: 100), SubtaskSlice(Requests: 2, InputTokens: 200, OutputTokens: 100),
            turnId: turnId);
        var live = TurnUsage(Totals(Requests: 1, InputTokens: 50, OutputTokens: 25), SubtaskSlice(Requests: 0, InputTokens: 0, OutputTokens: 0),
            turnId: turnId);
        var chat = ChatUsage(Totals(Requests: 3, InputTokens: 250, OutputTokens: 125), SubtaskSlice(Requests: 2, InputTokens: 200, OutputTokens: 100),
            turns: [stored]);

        var stats = _calculator.Calculate(chat, live, [User(), Assistant()], isRunning: true, ChatWidgetScope.LastTurn);

        stats.Requests.ShouldBe(2);
        stats.InputTokens.ShouldBe(200);
    }

    [Fact]
    public void ShouldCountReasoningTokens()
    {
        var chat = ChatUsage(
            Totals(Requests: 2, InputTokens: 200, OutputTokens: 100, ReasoningTokens: 30),
            SubtaskSlice(Requests: 2, InputTokens: 200, OutputTokens: 100, ReasoningTokens: 30));

        var stats = _calculator.Calculate(chat, null, [User(), Assistant()], isRunning: false, ChatWidgetScope.Chat);

        stats.ReasoningTokens.ShouldBe(30);
    }

    [Fact]
    public void ShouldCountTurnsThatDelegatedAcrossTheWholeChat()
    {
        var with = TurnUsage(Totals(Requests: 2, InputTokens: 100, OutputTokens: 50), SubtaskSlice(Requests: 2, InputTokens: 100, OutputTokens: 50));
        var without = TurnUsage(Totals(Requests: 1, InputTokens: 50, OutputTokens: 25), SubtaskSlice(Requests: 0, InputTokens: 0, OutputTokens: 0));
        var chat = ChatUsage(Totals(Requests: 3, InputTokens: 150, OutputTokens: 75), SubtaskSlice(Requests: 2, InputTokens: 100, OutputTokens: 50),
            turns: [without, with]);

        var stats = _calculator.Calculate(chat, null, [User(), Assistant(), User(), Assistant()], isRunning: false,
            ChatWidgetScope.Chat);

        stats.TurnsWithSubtasks.ShouldBe(1);
        stats.Turns.ShouldBe(2);
    }

    private static ChatMessageView User() => User(T0);
    private static ChatMessageView Assistant() => Assistant(T0);
    private static ChatMessageView User(DateTimeOffset at) => new(Guid.NewGuid(), null, "User", "Hi", at);
    private static ChatMessageView Assistant(DateTimeOffset at) => new(Guid.NewGuid(), null, "Assistant", "answer", at);

    private static TokenUsageTotals Totals(int Requests, long InputTokens, long OutputTokens, long ReasoningTokens = 0) =>
        new(new TokenCounts(InputTokens, OutputTokens, 0, ReasoningTokens), Requests, 0, null, 0, 0);

    private static TokenUsageSlice SubtaskSlice(int Requests, long InputTokens, long OutputTokens, long ReasoningTokens = 0) =>
        new(nameof(TokenUsagePurpose.Subtask),
            new TokenUsageTotals(new TokenCounts(InputTokens, OutputTokens, 0, ReasoningTokens), Requests, 0, null, 0, 0));

    private static ChatTokenUsage ChatUsage(TokenUsageTotals totals, TokenUsageSlice subtaskSlice,
        IReadOnlyList<TurnTokenUsage>? turns = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), totals, [subtaskSlice], turns ?? []);

    private static TurnTokenUsage TurnUsage(TokenUsageTotals totals, TokenUsageSlice subtaskSlice, Guid? turnId = null) =>
        new(turnId ?? Guid.NewGuid(), Guid.NewGuid(), T0, totals, [subtaskSlice]);
}
