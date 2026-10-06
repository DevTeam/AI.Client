namespace AI.Web.Tests.Widgets;

using AI.Contracts.Chats;
using AI.Contracts.Usage;
using AI.Web.Widgets;
using Shouldly;
using Xunit;

public class ChatModelStatisticsCalculatorTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private readonly ChatModelStatisticsCalculator _calculator = new();

    [Fact]
    public void ShouldGroupAnswerRequestsByModelAndOrderByRequests()
    {
        var chat = ChatUsage(
            Turn(Answers: [Answer("gpt-5", T0), Answer("gpt-5", T0.AddMinutes(1)), Answer("claude", T0.AddMinutes(2))]));

        var stats = _calculator.Calculate(chat, null, [User(), Assistant()], isRunning: false, ChatWidgetScope.Chat);

        stats.Models.Select(model => (model.Model, model.Requests)).ShouldBe([("gpt-5", 2), ("claude", 1)]);
        stats.Models[0].Share.ShouldBe(2d / 3, 0.0001);
        stats.Requests.ShouldBe(3);
        stats.Turns.ShouldBe(1);
        stats.TurnsWithAnswers.ShouldBe(1);
    }

    [Fact]
    public void ShouldKeepTwoSpellingsOfOneModelApart()
    {
        var chat = ChatUsage(Turn(Answers: [Answer("GPT-5", T0), Answer("gpt-5", T0)]));

        var stats = _calculator.Calculate(chat, null, [User(), Assistant()], isRunning: false, ChatWidgetScope.Chat);

        stats.Models.Count.ShouldBe(2);
    }

    [Fact]
    public void ShouldRecordTheFirstAndLastRequestOfAModel()
    {
        var early = T0.AddMinutes(1);
        var late = T0.AddMinutes(9);
        var chat = ChatUsage(Turn(Answers: [Answer("m", late), Answer("m", early)]));

        var model = _calculator.Calculate(chat, null, [User(), Assistant()], isRunning: false,
            ChatWidgetScope.Chat).Models.ShouldHaveSingleItem();

        model.FirstAt.ShouldBe(early);
        model.LastAt.ShouldBe(late);
    }

    [Fact]
    public void ShouldSkipBlankModelNames()
    {
        var chat = ChatUsage(Turn(Answers: [Answer(" ", T0), Answer("m", T0)]));

        var stats = _calculator.Calculate(chat, null, [User(), Assistant()], isRunning: false, ChatWidgetScope.Chat);

        stats.Models.ShouldHaveSingleItem().Model.ShouldBe("m");
        stats.Requests.ShouldBe(1);
    }

    [Fact]
    public void ShouldCountOnlyTheLastTurnWhenAsked()
    {
        var chat = ChatUsage(
            Turn(Answers: [Answer("old", T0)]),
            Turn(Answers: [Answer("new", T0.AddMinutes(5))]));

        var stats = _calculator.Calculate(chat, null, [User(), Assistant(), User(), Assistant()],
            isRunning: false, ChatWidgetScope.LastTurn);

        stats.Models.ShouldHaveSingleItem().Model.ShouldBe("new");
        stats.Turns.ShouldBe(1);
        stats.Requests.ShouldBe(1);
    }

    [Fact]
    public void ShouldPreferTheLiveTurnWhenItIsAtLeastAsBusy()
    {
        var turnId = Guid.NewGuid();
        var stored = Turn(turnId, [Answer("stored", T0)]);
        var chat = ChatUsage(stored);
        var live = Turn(turnId, [Answer("live", T0), Answer("live", T0)]);

        var stats = _calculator.Calculate(chat, live, [User(), Assistant()], isRunning: true, ChatWidgetScope.LastTurn);

        stats.Models.ShouldHaveSingleItem().Model.ShouldBe("live");
        stats.Requests.ShouldBe(2);
        stats.IsRunning.ShouldBeTrue();
    }

    [Fact]
    public void ShouldKeepTheStoredTurnWhenTheLiveOneIsBehind()
    {
        var turnId = Guid.NewGuid();
        var stored = Turn(turnId, [Answer("stored", T0), Answer("stored", T0)]);
        var chat = ChatUsage(stored);
        var live = Turn(turnId, [Answer("live", T0)]);

        var stats = _calculator.Calculate(chat, live, [User(), Assistant()], isRunning: true, ChatWidgetScope.LastTurn);

        stats.Models.ShouldHaveSingleItem().Model.ShouldBe("stored");
    }

    [Fact]
    public void ShouldPreferALiveTurnThatStartedLater()
    {
        var chat = ChatUsage(Turn(Answers: [Answer("stored", T0)]));
        var live = Turn(Guid.NewGuid(), [Answer("live", T0.AddMinutes(2))]);

        var stats = _calculator.Calculate(chat, live, [User(), Assistant()], isRunning: true, ChatWidgetScope.LastTurn);

        stats.Models.ShouldHaveSingleItem().Model.ShouldBe("live");
    }

    [Fact]
    public void ShouldSaySoFarOnlyForARunningLastTurn()
    {
        var chat = ChatUsage(Turn(Answers: [Answer("m", T0)]));
        IReadOnlyList<ChatMessageView> branch = [User(), Assistant()];

        _calculator.Calculate(chat, null, branch, isRunning: true, ChatWidgetScope.Chat).IsRunning.ShouldBeFalse();
        _calculator.Calculate(chat, null, branch, isRunning: true, ChatWidgetScope.LastTurn).IsRunning.ShouldBeTrue();
    }

    [Fact]
    public void ShouldCountTurnsWithAnswersAcrossTheChat()
    {
        var chat = ChatUsage(
            Turn(Answers: [Answer("m", T0)]),
            Turn(Answers: null),
            Turn(Answers: [Answer("m", T0.AddMinutes(3))]));

        var stats = _calculator.Calculate(chat, null, [User(), Assistant(), User(), Assistant(), User(), Assistant()],
            isRunning: false, ChatWidgetScope.Chat);

        stats.Turns.ShouldBe(3);
        stats.TurnsWithAnswers.ShouldBe(2);
        stats.Requests.ShouldBe(2);
    }

    [Fact]
    public void ShouldReportAnEmptyStateWithoutUsage()
    {
        var stats = _calculator.Calculate(null, null, [User()], isRunning: false, ChatWidgetScope.Chat);

        stats.Models.ShouldBeEmpty();
        stats.Requests.ShouldBe(0);
        stats.Turns.ShouldBe(1);
        stats.TurnsWithAnswers.ShouldBe(0);
    }

    [Fact]
    public void ShouldSplitTurnsAtUserMessages()
    {
        // Whatever comes before the first user message belongs to the opening turn.
        var stats = _calculator.Calculate(null, null, [Assistant(), Assistant()], isRunning: false,
            ChatWidgetScope.Chat);

        stats.Turns.ShouldBe(1);
    }

    private static ChatMessageView User() => new(Guid.NewGuid(), null, "User", "Hi", T0);

    private static ChatMessageView Assistant() => new(Guid.NewGuid(), null, "Assistant", "answer", T0);

    private static AnswerModelUsage Answer(string model, DateTimeOffset at) => new(Guid.NewGuid(), at, model);

    private static TurnTokenUsage Turn(IReadOnlyList<AnswerModelUsage>? Answers) =>
        Turn(Guid.NewGuid(), Answers);

    private static TurnTokenUsage Turn(Guid turnId, IReadOnlyList<AnswerModelUsage>? Answers) =>
        new(turnId, Guid.NewGuid(), T0, Totals(Answers?.Count ?? 0), [], Answers);

    private static ChatTokenUsage ChatUsage(params TurnTokenUsage[] turns) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Totals((int)turns.Sum(turn => turn.Totals.Requests)), [], turns);

    private static TokenUsageTotals Totals(int requests) => new(new TokenCounts(100, 50), requests, 0, null, 0, 1_000);
}
