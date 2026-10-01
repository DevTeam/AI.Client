namespace AI.Web.Tests.Widgets;

using AI.Contracts.Chats;
using AI.Contracts.Settings;
using AI.Contracts.Usage;
using AI.Web.Composer;
using AI.Web.Usage;
using AI.Web.Widgets;
using Shouldly;
using Xunit;

public class ChatPerformanceCalculatorTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private readonly ChatPerformanceCalculator _calculator =
        new(new UsagePresentation(new ComposerContextPresentation(new ConnectionContextLimitsResolver())));

    [Fact]
    public void ShouldReportWallClockAndActiveTimeForTheWholeChat()
    {
        var first = User(T0);
        var last = Assistant(T0.AddMinutes(11));
        var chat = ChatUsage(DurationMs: 4_000, OutputTokens: 800, ReasoningTokens: 200);

        var stats = _calculator.Calculate(chat, null, [first, Assistant(T0.AddMinutes(3)), User(T0.AddMinutes(5)),
            last], isRunning: false, ChatWidgetScope.Chat);

        stats.WallClockMs.ShouldBeGreaterThan(0);
        stats.ActiveMs.ShouldBe(4_000);
        stats.IdleMs.ShouldBeGreaterThan(0);
        stats.OutputSpeedTokensPerSecond.ShouldBe(200);
        stats.OutputTokens.ShouldBe(800);
        stats.ReasoningTokens.ShouldBe(200);
        stats.Requests.ShouldBe(1);
        stats.Turns.ShouldBe(2);
        stats.HasWallClock.ShouldBeTrue();
        stats.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public void ShouldTakeLastTurnOnlyWhenAsked()
    {
        var chat = ChatUsage(DurationMs: 5_000, OutputTokens: 100);
        var live = TurnUsage(TurnId: Guid.NewGuid(), DurationMs: 1_500, OutputTokens: 300);

        var stats = _calculator.Calculate(chat, live, [User(), Assistant(), User(), Assistant()], isRunning: true,
            ChatWidgetScope.LastTurn);

        stats.Turns.ShouldBe(1);
        stats.ActiveMs.ShouldBe(1_500);
        stats.OutputTokens.ShouldBe(300);
        stats.OutputSpeedTokensPerSecond.ShouldBe(200);
        stats.IsRunning.ShouldBeTrue();
    }

    [Fact]
    public void ShouldPreferLiveTurnWhenItHasMoreRequestsThanStored()
    {
        var turnId = Guid.NewGuid();
        var stored = TurnUsage(TurnId: turnId, DurationMs: 1_000, OutputTokens: 50, Requests: 1);
        var live = TurnUsage(TurnId: turnId, DurationMs: 2_000, OutputTokens: 200, Requests: 2);
        var chat = new ChatTokenUsage(Guid.NewGuid(), Guid.NewGuid(), Totals(DurationMs: 1_000, OutputTokens: 50), [],
            [stored]);

        var stats = _calculator.Calculate(chat, live, [User(), Assistant()], isRunning: true, ChatWidgetScope.LastTurn);

        stats.ActiveMs.ShouldBe(2_000);
        stats.OutputTokens.ShouldBe(200);
    }

    [Fact]
    public void ShouldKeepStoredTurnWhenLiveIsNotAhead()
    {
        var turnId = Guid.NewGuid();
        var stored = TurnUsage(TurnId: turnId, DurationMs: 2_000, OutputTokens: 200, Requests: 2);
        var live = TurnUsage(TurnId: turnId, DurationMs: 1_000, OutputTokens: 50, Requests: 1);
        var chat = new ChatTokenUsage(Guid.NewGuid(), Guid.NewGuid(),
            Totals(DurationMs: 2_000, OutputTokens: 200, Requests: 2), [], [stored]);

        var stats = _calculator.Calculate(chat, live, [User(), Assistant()], isRunning: true, ChatWidgetScope.LastTurn);

        stats.ActiveMs.ShouldBe(2_000);
        stats.OutputTokens.ShouldBe(200);
    }

    [Fact]
    public void ShouldLeaveEmptyWhenThereIsNoUserMessage()
    {
        var stats = _calculator.Calculate(null, null, [Assistant()], isRunning: false, ChatWidgetScope.Chat);

        stats.Requests.ShouldBe(0);
        stats.HasWallClock.ShouldBeFalse();
        stats.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public void ShouldSuppressSpeedWhenTooLittleWasMeasured()
    {
        var chat = ChatUsage(DurationMs: 100, OutputTokens: 50);

        var stats = _calculator.Calculate(chat, null, [User(), Assistant()], isRunning: false, ChatWidgetScope.Chat);

        stats.ActiveMs.ShouldBe(100);
        stats.OutputSpeedTokensPerSecond.ShouldBeNull();
    }

    private static ChatMessageView User() => User(T0);
    private static ChatMessageView Assistant() => Assistant(T0);
    private static ChatMessageView User(DateTimeOffset at) => new(Guid.NewGuid(), null, "User", "Hi", at);
    private static ChatMessageView Assistant(DateTimeOffset at) => new(Guid.NewGuid(), null, "Assistant", "answer", at);

    private static ChatTokenUsage ChatUsage(long DurationMs, long OutputTokens, long ReasoningTokens = 0, int Requests = 1)
    {
        var totals = Totals(DurationMs: DurationMs, OutputTokens: OutputTokens,
            ReasoningTokens: ReasoningTokens, Requests: Requests);
        return new ChatTokenUsage(Guid.NewGuid(), Guid.NewGuid(), totals, [], []);
    }

    private static TurnTokenUsage TurnUsage(Guid TurnId, long DurationMs, long OutputTokens, long ReasoningTokens = 0,
        int Requests = 1) =>
        new(TurnId, Guid.NewGuid(), T0,
            Totals(DurationMs: DurationMs, OutputTokens: OutputTokens, ReasoningTokens: ReasoningTokens, Requests: Requests), []);

    private static TokenUsageTotals Totals(long DurationMs, long OutputTokens, long ReasoningTokens = 0, int Requests = 1) =>
        new(new TokenCounts(0, OutputTokens, 0, ReasoningTokens), Requests, 0, null, 0, DurationMs);
}
