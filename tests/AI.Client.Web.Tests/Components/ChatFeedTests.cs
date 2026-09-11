using AI.Client.Contracts.Chat;
using AI.Client.Contracts.Chats;
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

        items.Count.ShouldBe(3);
        items[0].Message.ShouldBe(question);
        items[0].IsPreamble.ShouldBeFalse();
        items[1].IsPreamble.ShouldBeTrue();
        items[1].Message.ShouldBe(preamble);
        items[2].ToolGroup.ShouldBe(new[] { preamble, result });
    }

    [Fact]
    public void ShouldNotShowMessageForAssistantWithOnlyToolCalls()
    {
        var silent = Assistant(string.Empty, "call-1");

        var items = ChatFeed.BuildFeedItems([silent, ToolResult("call-1")]);

        items.Count.ShouldBe(1);
        items[0].ToolGroup.ShouldNotBeNull();
    }

    [Fact]
    public void ShouldNotTreatWhitespaceOnlyContentAsPreamble()
    {
        var blank = Assistant("   \n  ", "call-1");

        var items = ChatFeed.BuildFeedItems([blank, ToolResult("call-1")]);

        items.Count.ShouldBe(1);
        items[0].ToolGroup.ShouldNotBeNull();
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

        items.Select(item => item switch
        {
            { ToolGroup: not null } => "tools",
            { IsPreamble: true } => "preamble",
            _ => "message",
        }).ShouldBe(["message", "preamble", "tools", "preamble", "tools", "message"]);

        // The second cycle's group must not swallow the first cycle's results, and vice versa.
        items[2].ToolGroup.ShouldBe(new[] { first, firstResult });
        items[4].ToolGroup.ShouldBe(new[] { second, secondResult });
    }

    [Fact]
    public void ShouldRenderFinalAssistantMessageAsPlainMessage()
    {
        var answer = Assistant("Готово.");

        var items = ChatFeed.BuildFeedItems([answer]);

        items.ShouldHaveSingleItem();
        items[0].IsPreamble.ShouldBeFalse();
        items[0].Message.ShouldBe(answer);
        items[0].ToolGroup.ShouldBeNull();
    }

    [Fact]
    public void ShouldShowPreambleAndOpenGroupWhileResultIsPending()
    {
        // Mid-run: the call was issued, nothing has come back yet. The group's anchor (its last
        // message) is the preamble itself, which is why the markup gives the two items distinct
        // DOM ids rather than rendering the same id twice.
        var preamble = Assistant("Читаю файл.", "call-1");

        var items = ChatFeed.BuildFeedItems([preamble]);

        items.Count.ShouldBe(2);
        items[0].IsPreamble.ShouldBeTrue();
        items[1].ToolGroup.ShouldBe(new[] { preamble });
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
    }

    [Fact]
    public void ShouldMatchToolResultsByCallIdAndLeavePendingCallsUnresolved()
    {
        var preamble = Assistant("Читаю файлы.", "call-1", "call-2");
        var onlyResult = ToolResult("call-2", "second");

        var pairs = ChatFeed.BuildToolPairs([preamble, onlyResult]);

        pairs.Count.ShouldBe(2);
        pairs[0].Call.Id.ShouldBe("call-1");
        pairs[0].Result.ShouldBeNull();
        pairs[1].Call.Id.ShouldBe("call-2");
        pairs[1].Result.ShouldBe(onlyResult);
    }
}
