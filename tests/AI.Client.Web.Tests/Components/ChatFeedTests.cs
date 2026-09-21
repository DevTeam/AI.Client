using AI.Client.Contracts.Chat;
using AI.Client.Contracts.Chats;
using AI.Client.Contracts.Workspace;
using AI.Client.Web.Components;
using Shouldly;
using Xunit;

namespace AI.Client.Web.Tests.Components;

public sealed class ChatFeedTests
{
    private static DateTimeOffset _clock = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ChatMessageView Message(
        string role,
        string content,
        IReadOnlyList<ChatToolCall>? toolCalls = null,
        string? toolCallId = null)
    {
        _clock = _clock.AddSeconds(1);
        return new ChatMessageView(Guid.NewGuid(), null, role, content, _clock, ToolCalls: toolCalls, ToolCallId: toolCallId);
    }

    private static ChatMessageView User(string content) => Message("User", content);

    private static ChatMessageView Assistant(string content, params string[] callIds) =>
        Message("Assistant", content, callIds.Length == 0 ? null : callIds.Select(id => new ChatToolCall(id, "read_text_file", "{}")).ToArray());

    private static ChatMessageView ToolResult(string callId, string content = "ok") =>
        Message("Tool", content, toolCallId: callId);

    [Fact]
    public void ShouldKeepAssistantContentVisibleWhileGroupingItsToolCalls()
    {
        var preamble = Assistant("Проверю путь сообщения от модели до рендера ленты.", "call-1");
        var result = ToolResult("call-1");

        var question = User("Разберись");

        var items = ChatFeed.BuildFeedItems([question, preamble, result]);

        // The explanation and the calls it announced are one block: the group carries both, and
        // the markup renders the text above the tool rows.
        items.Count.ShouldBe(2);
        items[0].Message.ShouldBe(question);
        items[1].ToolGroup.ShouldBe(new[] { preamble, result });
        ChatFeed.PreambleOf(items[1].ToolGroup!).ShouldBe(preamble);
    }

    [Fact]
    public void ShouldNotShowMessageForAssistantWithOnlyToolCalls()
    {
        var silent = Assistant(string.Empty, "call-1");

        var items = ChatFeed.BuildFeedItems([silent, ToolResult("call-1")]);

        items.Count.ShouldBe(1);
        ChatFeed.PreambleOf(items[0].ToolGroup!).ShouldBeNull();
    }

    [Fact]
    public void ShouldNotTreatWhitespaceOnlyContentAsPreamble()
    {
        var blank = Assistant("   \n  ", "call-1");

        var items = ChatFeed.BuildFeedItems([blank, ToolResult("call-1")]);

        items.Count.ShouldBe(1);
        ChatFeed.PreambleOf(items[0].ToolGroup!).ShouldBeNull();
    }

    [Fact]
    public void ShouldKeepPreambleThenToolsOrderAcrossSeveralCycles()
    {
        var question = User("Разберись");
        var first = Assistant("Проверю путь сообщения.", "call-1");
        var firstResult = ToolResult("call-1");
        var second = Assistant("Сообщения сохраняются. Теперь проверю группировку.", "call-2");
        var secondResult = ToolResult("call-2");
        var answer = Assistant("Причина в условии группировки.");

        var items = ChatFeed.BuildFeedItems([question, first, firstResult, second, secondResult, answer]);

        items.Select(item => item.ToolGroup is null ? "message" : "cycle")
            .ShouldBe(["message", "cycle", "cycle", "message"]);

        // A preamble opens its own cycle, so the second group must not swallow the first cycle's
        // results, and vice versa.
        items[1].ToolGroup.ShouldBe(new[] { first, firstResult });
        items[2].ToolGroup.ShouldBe(new[] { second, secondResult });
        ChatFeed.PreambleOf(items[1].ToolGroup!).ShouldBe(first);
        ChatFeed.PreambleOf(items[2].ToolGroup!).ShouldBe(second);
    }

    [Fact]
    public void ShouldRenderFinalAssistantMessageAsPlainMessage()
    {
        var answer = Assistant("Готово.");

        var items = ChatFeed.BuildFeedItems([answer]);

        items.ShouldHaveSingleItem();
        items[0].Message.ShouldBe(answer);
        items[0].ToolGroup.ShouldBeNull();
    }

    [Fact]
    public void ShouldShowPreambleAndItsGroupWhileTheResultIsPending()
    {
        // Mid-run: the call was issued, nothing has come back yet. One block still, so the
        // explanation is on screen before its result exists.
        var preamble = Assistant("Читаю файл.", "call-1");

        var items = ChatFeed.BuildFeedItems([preamble]);

        items.ShouldHaveSingleItem();
        items[0].ToolGroup.ShouldBe(new[] { preamble });
        ChatFeed.PreambleOf(items[0].ToolGroup!).ShouldBe(preamble);
    }

    [Fact]
    public void ShouldCollapseConsecutiveSilentToolCyclesIntoOneGroup()
    {
        var first = Assistant(string.Empty, "call-1");
        var firstResult = ToolResult("call-1");
        var second = Assistant(string.Empty, "call-2");
        var secondResult = ToolResult("call-2");

        var items = ChatFeed.BuildFeedItems([first, firstResult, second, secondResult]);

        items.ShouldHaveSingleItem();
        items[0].ToolGroup!.Count.ShouldBe(4);
        ChatFeed.PreambleOf(items[0].ToolGroup!).ShouldBeNull();
    }

    [Fact]
    public void ShouldMatchToolResultsByCallIdAndLeavePendingCallsUnresolved()
    {
        var preamble = Assistant("Читаю файлы.", "call-1", "call-2");
        var onlyResult = ToolResult("call-2", "second");

        var invocations = ChatFeed.BuildInvocations([preamble, onlyResult]);

        invocations.Count.ShouldBe(2);
        invocations[0].Call.Id.ShouldBe("call-1");
        invocations[0].Result.ShouldBeNull();
        invocations[0].Duration.ShouldBeNull();
        invocations[1].Call.Id.ShouldBe("call-2");
        invocations[1].Result.ShouldBe(onlyResult);
    }

    [Fact]
    public void ShouldTimeEachCallFromWhenThePreviousOneLanded()
    {
        // Calls in a batch run in order and each result is persisted as it lands, so the gap
        // between landings is the call's own elapsed time — not the whole batch's.
        var batch = Assistant("Читаю файлы.", "call-1", "call-2");
        var first = ToolResult("call-1");
        var second = ToolResult("call-2");

        var invocations = ChatFeed.BuildInvocations([batch, first, second]);

        invocations[0].StartedAt.ShouldBe(batch.CreatedAt);
        invocations[0].CompletedAt.ShouldBe(first.CreatedAt);
        invocations[1].StartedAt.ShouldBe(first.CreatedAt);
        invocations[1].CompletedAt.ShouldBe(second.CreatedAt);
        // The helper advances its clock a second per message, so neither row inherits the other's.
        invocations[0].Duration.ShouldBe(TimeSpan.FromSeconds(1));
        invocations[1].Duration.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ShouldRenderTheTailOfALongTranscriptFirst()
    {
        var chain = Enumerable.Range(0, 30)
            .SelectMany(index => new[] { User($"question {index}"), Assistant($"answer {index}") })
            .ToArray();

        var items = ChatFeed.BuildFeedItems(chain);
        var tail = ChatFeed.TakeTail(items, 10);

        // The end of the transcript, in order — this is what the feed opens at.
        tail.Count.ShouldBe(10);
        tail.ShouldBe(items.Skip(items.Count - 10).ToArray());
    }

    [Fact]
    public void ShouldKeepEveryItemWhenTheTranscriptIsShorterThanTheLimit()
    {
        var items = ChatFeed.BuildFeedItems([User("one"), Assistant("two")]);

        ChatFeed.TakeTail(items, 10).ShouldBeSameAs(items);
        ChatFeed.TakeTail(items, int.MaxValue).ShouldBeSameAs(items);
    }

    [Fact]
    public void ShouldTreatAToolGroupAsOneTailItem()
    {
        var question = User("do it");
        var call = Assistant("checking", "call-1");
        var result = ToolResult("call-1");
        var answer = Assistant("done");

        var items = ChatFeed.BuildFeedItems([question, call, result, answer]);
        var tail = ChatFeed.TakeTail(items, 2);

        // The group is indivisible: a tail of two is "the tool block, then the answer", never
        // half a block.
        tail.Count.ShouldBe(2);
        tail[0].ToolGroup.ShouldBe(new[] { call, result });
        tail[1].Message.ShouldBe(answer);
    }

    [Fact]
    public void ShouldSplitTheFeedIntoTurnsAndKeepTheFinalAnswerSeparate()
    {
        var firstQuestion = User("first");
        var preamble = Assistant("checking", "call-1");
        var result = ToolResult("call-1");
        var firstAnswer = Assistant("done");
        var secondQuestion = User("second");
        var secondAnswer = Assistant("also done");

        var turns = ChatFeed.BuildTurns([
            firstQuestion, preamble, result, firstAnswer, secondQuestion, secondAnswer
        ]);

        turns.Count.ShouldBe(2);
        turns[0].UserMessage.ShouldBe(firstQuestion);
        turns[0].IntermediateItems.ShouldHaveSingleItem();
        turns[0].IntermediateItems[0].ToolGroup.ShouldBe(new[] { preamble, result });
        turns[0].FinalAnswer!.Value.Message.ShouldBe(firstAnswer);
        turns[1].UserMessage.ShouldBe(secondQuestion);
        turns[1].IntermediateItems.ShouldBeEmpty();
        turns[1].FinalAnswer!.Value.Message.ShouldBe(secondAnswer);
    }

    [Fact]
    public void ShouldTreatEarlierPlainAssistantMessagesAsIntermediate()
    {
        var question = User("do it");
        var progress = Assistant("still working");
        var answer = Assistant("finished");

        var turn = ChatFeed.BuildTurns([question, progress, answer]).ShouldHaveSingleItem();

        turn.IntermediateItems.ShouldHaveSingleItem();
        turn.IntermediateItems[0].Message.ShouldBe(progress);
        turn.FinalAnswer!.Value.Message.ShouldBe(answer);
    }

    [Fact]
    public void ShouldAllowATurnToEndWithoutAFinalAnswer()
    {
        var question = User("do it");
        var preamble = Assistant("working", "call-1");
        var result = ToolResult("call-1");

        var turn = ChatFeed.BuildTurns([question, preamble, result]).ShouldHaveSingleItem();

        turn.IntermediateItems.ShouldHaveSingleItem();
        turn.FinalAnswer.ShouldBeNull();
        turn.LastMessage.ShouldBe(result);
    }

    [Fact]
    public void ShouldKeepLatestPlainAssistantMessageIntermediateWhileTurnIsUnfinished()
    {
        var question = User("do it");
        var firstProgress = Assistant("checking", "call-1");
        var result = ToolResult("call-1");
        var latestProgress = Assistant("now checking usages");

        var turn = ChatFeed.BuildTurns(
            [question, firstProgress, result, latestProgress],
            lastTurnEndedWithoutFinalAnswer: true).ShouldHaveSingleItem();

        turn.IntermediateItems.Count.ShouldBe(2);
        turn.IntermediateItems[1].Message.ShouldBe(latestProgress);
        turn.FinalAnswer.ShouldBeNull();
        turn.LastMessage.ShouldBe(latestProgress);
    }

    [Fact]
    public void ShouldUseLatestIntermediateAssistantTextForRunningTitle()
    {
        var question = User("do it");
        var firstProgress = Assistant("checking files", "call-1");
        var result = ToolResult("call-1");
        var latestProgress = Assistant("  now\nchecking   usages  ");
        var turn = ChatFeed.BuildTurns(
            [question, firstProgress, result, latestProgress],
            lastTurnEndedWithoutFinalAnswer: true).ShouldHaveSingleItem();

        ChatFeed.RunningTitleOf(turn, TimeSpan.FromSeconds(63))
            .ShouldBe("now checking usages · 1m 3s");
    }

    [Fact]
    public void ShouldReturnNoRunningTitleBeforeTheModelReportsAnAction()
    {
        var turn = ChatFeed.BuildTurns([User("do it")]).ShouldHaveSingleItem();

        ChatFeed.RunningTitleOf(turn, TimeSpan.FromSeconds(2)).ShouldBeNull();
    }

    [Fact]
    public void ShouldSuppressLiveWorkspaceStatisticsOnceTheLastTurnHasADurableReceipt()
    {
        var oldAnswer = Assistant("old") with
        {
            WorkspaceChanges = new WorkspaceChangeSet(
                [new FileChange("old.cs", FileChangeKind.Modified, 1, 0)], 1, 0)
        };
        var question = User("new work");
        var finalAnswer = Assistant("done") with
        {
            WorkspaceChanges = new WorkspaceChangeSet(
                [new FileChange("new.cs", FileChangeKind.Modified, 2, 1)], 2, 1)
        };

        ChatFeed.LastTurnHasWorkspaceReceipt([oldAnswer, question]).ShouldBeFalse();
        ChatFeed.LastTurnHasWorkspaceReceipt([oldAnswer, question, finalAnswer]).ShouldBeTrue();
        ChatFeed.LastTurnHasCompleteAnswer([oldAnswer, question]).ShouldBeFalse();
        ChatFeed.LastTurnHasCompleteAnswer([oldAnswer, question, finalAnswer]).ShouldBeTrue();
    }

    [Fact]
    public void ShouldFormatCompactTurnTimeWithoutFractionalSeconds()
    {
        ChatFeed.FormatDuration(TimeSpan.FromSeconds(14.9)).ShouldBe("14s");
        ChatFeed.FormatDuration(TimeSpan.FromSeconds(63.8)).ShouldBe("1m 3s");
    }
}
