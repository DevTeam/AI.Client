namespace AI.Web.Tests.Widgets;

using AI.Contracts.Chat;
using AI.Contracts.Chats;
using AI.Contracts.Usage;
using AI.Contracts.Workspace;
using AI.Web.Widgets;
using Shouldly;
using Xunit;

public class ChatTimelineStatisticsCalculatorTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private readonly ChatTimelineStatisticsCalculator _calculator = new();

    [Fact]
    public void ShouldReturnEmptyForEmptyBranch()
    {
        var stats = _calculator.Calculate([], null, null, isRunning: false);

        stats.ShouldBe(ChatTimelineStatistics.Empty);
    }

    [Fact]
    public void ShouldReportOneTurnForAssistantOnlyOpening()
    {
        // The opening assistant turn that precedes the first user message is one turn with no
        // user message to scroll to and no duration (single message).
        var stats = _calculator.Calculate([Assistant(T0)], null, null, isRunning: false);

        stats.HasTurns.ShouldBeTrue();
        stats.Turns.Count.ShouldBe(1);
        stats.Turns[0].TurnIndex.ShouldBe(1);
        stats.Turns[0].UserMessageId.ShouldBeNull();
        stats.Turns[0].HasDuration.ShouldBeFalse();
        stats.Turns[0].IsRunning.ShouldBeFalse();
    }

    [Fact]
    public void ShouldSplitBranchAtUserMessages()
    {
        var stats = _calculator.Calculate(
            [User(T0), Assistant(T0.AddSeconds(5)), User(T0.AddSeconds(30)), Assistant(T0.AddSeconds(50))],
            null, null, isRunning: false);

        stats.Turns.Count.ShouldBe(2);
        stats.Turns[0].TurnIndex.ShouldBe(1);
        stats.Turns[0].DurationMs.ShouldBe(5_000);
        stats.Turns[1].TurnIndex.ShouldBe(2);
        stats.Turns[1].DurationMs.ShouldBe(20_000);
        stats.Turns[0].UserMessageId.ShouldNotBeNull();
        stats.Turns[1].UserMessageId.ShouldNotBeNull();
        stats.Turns[^1].IsRunning.ShouldBeFalse();
    }

    [Fact]
    public void ShouldMarkNoDurationWhenTurnHasOneMessage()
    {
        var stats = _calculator.Calculate([User(T0)], null, null, isRunning: false);

        stats.Turns[0].HasDuration.ShouldBeFalse();
        stats.Turns[0].DurationMs.ShouldBe(0);
    }

    [Fact]
    public void ShouldUseUserCreatedAtAsStartedAt()
    {
        var started = T0.AddMinutes(2);
        var answered = T0.AddMinutes(5);

        var stats = _calculator.Calculate([User(started), Assistant(answered)], null, null, isRunning: false);

        stats.Turns[0].StartedAt.ShouldBe(started);
        stats.Turns[0].DurationMs.ShouldBe((long)(answered - started).TotalMilliseconds);
    }

    [Fact]
    public void ShouldAttachPerTurnUsageByPosition()
    {
        var firstTurn = TurnUsage(Totals(Requests: 1, InputTokens: 100, OutputTokens: 50));
        var secondTurn = TurnUsage(Totals(Requests: 2, InputTokens: 200, OutputTokens: 80));
        var chat = ChatUsage(Totals(Requests: 3, InputTokens: 300, OutputTokens: 130),
            turns: [firstTurn, secondTurn]);

        var stats = _calculator.Calculate(
            [User(T0), Assistant(T0.AddSeconds(5)), User(T0.AddSeconds(30)), Assistant(T0.AddSeconds(50))],
            chat, null, isRunning: false);

        stats.HasProviderData.ShouldBeTrue();
        stats.Turns[0].Requests.ShouldBe(1);
        stats.Turns[0].InputTokens.ShouldBe(100);
        stats.Turns[0].OutputTokens.ShouldBe(50);
        stats.Turns[1].Requests.ShouldBe(2);
        stats.Turns[1].InputTokens.ShouldBe(200);
        stats.Turns[1].OutputTokens.ShouldBe(80);
    }

    [Fact]
    public void ShouldLeaveProviderFieldsNullWhenSliceMissing()
    {
        var firstTurn = TurnUsage(Totals(Requests: 1, InputTokens: 100, OutputTokens: 50));
        var chat = ChatUsage(Totals(Requests: 1, InputTokens: 100, OutputTokens: 50),
            turns: [firstTurn]);

        var stats = _calculator.Calculate(
            [User(T0), Assistant(T0.AddSeconds(5)), User(T0.AddSeconds(30)), Assistant(T0.AddSeconds(50))],
            chat, null, isRunning: false);

        stats.HasProviderData.ShouldBeTrue();
        stats.Turns[1].Requests.ShouldBeNull();
        stats.Turns[1].InputTokens.ShouldBeNull();
        stats.Turns[1].OutputTokens.ShouldBeNull();
    }

    [Fact]
    public void ShouldPreferLiveTurnWhenItStartsNoEarlierAndIsAtLeastAsFar()
    {
        var stored = TurnUsage(Totals(Requests: 1, InputTokens: 100, OutputTokens: 50),
            startedAt: T0.AddMinutes(1));
        var live = TurnUsage(Totals(Requests: 3, InputTokens: 400, OutputTokens: 200),
            startedAt: T0.AddMinutes(1));
        var chat = ChatUsage(Totals(Requests: 4, InputTokens: 500, OutputTokens: 250),
            turns: [stored]);

        var stats = _calculator.Calculate(
            [User(T0), Assistant(T0.AddSeconds(5)), User(T0.AddMinutes(1)), Assistant(T0.AddMinutes(2))],
            chat, live, isRunning: true);

        stats.HasProviderData.ShouldBeTrue();
        stats.Turns[1].Requests.ShouldBe(3);
        stats.Turns[1].InputTokens.ShouldBe(400);
        stats.Turns[1].OutputTokens.ShouldBe(200);
        stats.Turns[1].IsRunning.ShouldBeTrue();
    }

    [Fact]
    public void ShouldKeepStoredTurnWhenLiveIsNotAhead()
    {
        // Live could replace the last stored slice, but starts earlier — the same rule the
        // Subtasks widget uses to decide between stored and live.
        var storedFirst = TurnUsage(Totals(Requests: 1, InputTokens: 100, OutputTokens: 50),
            startedAt: T0);
        var storedLast = TurnUsage(Totals(Requests: 2, InputTokens: 200, OutputTokens: 100),
            startedAt: T0.AddMinutes(2));
        var live = TurnUsage(Totals(Requests: 3, InputTokens: 400, OutputTokens: 200),
            startedAt: T0.AddMinutes(1));
        var chat = ChatUsage(Totals(Requests: 3, InputTokens: 300, OutputTokens: 150),
            turns: [storedFirst, storedLast]);

        var stats = _calculator.Calculate(
            [User(T0), Assistant(T0.AddSeconds(5)), User(T0.AddMinutes(2)), Assistant(T0.AddMinutes(3))],
            chat, live, isRunning: true);

        stats.Turns[1].Requests.ShouldBe(2);
        stats.Turns[1].InputTokens.ShouldBe(200);
        stats.Turns[1].IsRunning.ShouldBeTrue();
    }

    [Fact]
    public void ShouldMarkLastTurnRunningOnlyWhileRunIsOn()
    {
        var stats = _calculator.Calculate(
            [User(T0), Assistant(T0.AddSeconds(5)), User(T0.AddSeconds(30)), Assistant(T0.AddSeconds(50))],
            null, null, isRunning: false);

        stats.Turns[^1].IsRunning.ShouldBeFalse();
        stats.Turns[0].IsRunning.ShouldBeFalse();

        var running = _calculator.Calculate(
            [User(T0), Assistant(T0.AddSeconds(5)), User(T0.AddSeconds(30)), Assistant(T0.AddSeconds(50))],
            null, null, isRunning: true);

        running.Turns[^1].IsRunning.ShouldBeTrue();
        running.Turns[0].IsRunning.ShouldBeFalse();
    }

    [Fact]
    public void ShouldCollectToolCallsAndStripServerPrefix()
    {
        var first = AssistantWithToolCall("mcp_files__read_text_file", T0.AddSeconds(5));
        var second = AssistantWithToolCall("mcp_files__read_text_file", T0.AddSeconds(6));
        var third = AssistantWithToolCall("process_run", T0.AddSeconds(7));

        var stats = _calculator.Calculate([User(T0), first, second, third], null, null, isRunning: false);

        stats.Turns[0].ToolCalls.ShouldBe(["read_text_file", "process_run"]);
    }

    [Fact]
    public void ShouldKeepToolCallsDistinctAndInFirstSeenOrder()
    {
        var a = AssistantWithToolCall("process_run", T0.AddSeconds(5));
        var b = AssistantWithToolCall("read_text_file", T0.AddSeconds(6));
        var c = AssistantWithToolCall("process_run", T0.AddSeconds(7));

        var stats = _calculator.Calculate([User(T0), a, b, c], null, null, isRunning: false);

        stats.Turns[0].ToolCalls.ShouldBe(["process_run", "read_text_file"]);
    }

    [Fact]
    public void ShouldCollectFilesChangedAcrossTurn()
    {
        var first = AssistantWithFiles(
            [Change("src/a.cs"), Change("src/b.cs")],
            T0.AddSeconds(5));
        var second = AssistantWithFiles(
            [Change("src/a.cs"), Change("src/c.cs")],
            T0.AddSeconds(6));

        var stats = _calculator.Calculate([User(T0), first, User(T0.AddSeconds(20)), second],
            null, null, isRunning: false);

        stats.Turns[0].FilesChanged.ShouldBe(["src/a.cs", "src/b.cs"]);
        stats.Turns[1].FilesChanged.ShouldBe(["src/a.cs", "src/c.cs"]);
    }

    [Fact]
    public void ShouldIgnoreEmptyWorkspaceChangeSets()
    {
        var assistant = Assistant(T0.AddSeconds(5));

        var stats = _calculator.Calculate([User(T0), assistant], null, null, isRunning: false);

        stats.Turns[0].FilesChanged.ShouldBeEmpty();
    }

    [Fact]
    public void ShouldReportNoProviderDataWhenLedgerIsEmpty()
    {
        var stats = _calculator.Calculate(
            [User(T0), Assistant(T0.AddSeconds(5)), User(T0.AddSeconds(30)), Assistant(T0.AddSeconds(50))],
            null, null, isRunning: false);

        stats.HasProviderData.ShouldBeFalse();
        stats.Turns[0].Requests.ShouldBeNull();
    }

    [Fact]
    public void ShouldSplitBranchTimeIntoInsideTurnsAndGapsWhenNoRequestWasTimed()
    {
        var stats = _calculator.Calculate(
            [User(T0), Assistant(T0.AddSeconds(5)), User(T0.AddSeconds(30)), Assistant(T0.AddSeconds(50))],
            null, null, isRunning: false);

        stats.Time.HasData.ShouldBeTrue();
        stats.Time.TotalMs.ShouldBe(50_000);
        stats.Time.Segments.ShouldBe([new TimelineTimeSegment("inside", 25_000), new TimelineTimeSegment("between", 25_000)]);
    }

    [Fact]
    public void ShouldTakeModelTimeFromTheProviderAndLeaveTheRestInsideTheTurn()
    {
        var first = TurnUsage(Totals(Requests: 1, InputTokens: 10, OutputTokens: 5, durationMs: 3_000));
        var second = TurnUsage(Totals(Requests: 1, InputTokens: 10, OutputTokens: 5, durationMs: 8_000),
            startedAt: T0.AddSeconds(30));
        var chat = ChatUsage(Totals(Requests: 2, InputTokens: 20, OutputTokens: 10, durationMs: 11_000),
            turns: [first, second]);

        var stats = _calculator.Calculate(
            [User(T0), Assistant(T0.AddSeconds(5)), User(T0.AddSeconds(30)), Assistant(T0.AddSeconds(50))],
            chat, null, isRunning: false);

        stats.Time.Segments.ShouldBe([
            new TimelineTimeSegment("model", 11_000),
            new TimelineTimeSegment("inside", 14_000),
            new TimelineTimeSegment("between", 25_000)
        ]);
        stats.Time.TotalMs.ShouldBe(50_000);
    }

    [Fact]
    public void ShouldClampModelTimeToTheTurnItServed()
    {
        // A request that outlasted the turn's messages must not push the other parts below zero.
        var turn = TurnUsage(Totals(Requests: 1, InputTokens: 10, OutputTokens: 5, durationMs: 90_000));
        var chat = ChatUsage(Totals(Requests: 1, InputTokens: 10, OutputTokens: 5, durationMs: 90_000),
            turns: [turn]);

        var stats = _calculator.Calculate([User(T0), Assistant(T0.AddSeconds(5))], chat, null, isRunning: false);

        stats.Time.Segments.ShouldBe([new TimelineTimeSegment("model", 5_000)]);
        stats.Time.TotalMs.ShouldBe(5_000);
    }

    [Fact]
    public void ShouldIgnoreNegativeGapsBetweenTurns()
    {
        // Timestamps of a legacy transcript may not be ordered; a gap that is not positive is zero.
        var stats = _calculator.Calculate(
            [User(T0.AddSeconds(60)), Assistant(T0.AddSeconds(70)), User(T0.AddSeconds(10)), Assistant(T0.AddSeconds(20))],
            null, null, isRunning: false);

        stats.Time.Segments.ShouldBe([new TimelineTimeSegment("inside", 20_000)]);
        stats.Time.TotalMs.ShouldBe(20_000);
    }

    [Fact]
    public void ShouldReportNoTimeBreakdownForASingleMessage()
    {
        var stats = _calculator.Calculate([User(T0)], null, null, isRunning: false);

        stats.Time.HasData.ShouldBeFalse();
        stats.Time.TotalMs.ShouldBe(0);
        stats.Time.Segments.ShouldBeEmpty();
    }

    private static ChatMessageView User(DateTimeOffset at) => new(Guid.NewGuid(), null, "User", "Hi", at);

    private static ChatMessageView Assistant(DateTimeOffset at) => new(Guid.NewGuid(), null, "Assistant", "answer", at);

    private static ChatMessageView AssistantWithToolCall(string name, DateTimeOffset at) =>
        new(Guid.NewGuid(), null, "Assistant", "", at,
            ToolCalls: [new ChatToolCall(Guid.NewGuid().ToString("n"), name, "{}")]);

    private static ChatMessageView AssistantWithFiles(IReadOnlyList<FileChange> files, DateTimeOffset at) =>
        new(Guid.NewGuid(), null, "Assistant", "", at,
            WorkspaceChanges: new WorkspaceChangeSet(files, 0, 0));

    private static FileChange Change(string path) => new(path, FileChangeKind.Modified, 1, 0);

    private static TokenUsageTotals Totals(int Requests, long InputTokens, long OutputTokens, long durationMs = 0) =>
        new(new TokenCounts(InputTokens, OutputTokens), Requests, 0, null, 0, durationMs);

    private static TurnTokenUsage TurnUsage(TokenUsageTotals totals, DateTimeOffset? startedAt = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), startedAt ?? T0, totals, []);

    private static ChatTokenUsage ChatUsage(TokenUsageTotals totals, IReadOnlyList<TurnTokenUsage>? turns = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), totals, [], turns ?? []);
}
