namespace AI.Web.Tests.Widgets;

using AI.Contracts.Chat;
using AI.Contracts.Chats;
using AI.Contracts.Runs;
using AI.Contracts.Tools;
using AI.Web.Widgets;
using Shouldly;
using Xunit;

public class ChatToolStatisticsCalculatorTests
{
    private readonly ChatToolStatisticsCalculator _calculator = new(new ToolResultCodec(new ToolResultModelProjector()));

    [Fact]
    public void ShouldGroupByFullToolNameAndCountResultsAndTurns()
    {
        var stats = Calculate([User(), Calls(new ChatToolCall("a", "mcp_one__read", "{}"), new("b", "mcp_two__read", "{}")),
            Result("a", "{\"isError\":false}"), Result("b", "{\"isError\":true,\"error\":\"Denied\"}"),
            User(), Calls(new ChatToolCall("c", "mcp_one__read", "{}")), Result("c", "{\"isError\":false}")]);

        (stats.Calls, stats.Turns, stats.TurnsWithTools).ShouldBe((3, 2, 2));
        stats.Tools.Select(tool => (tool.Name, tool.Calls, tool.Errors))
            .ShouldBe([("mcp_one__read", 2, 0), ("mcp_two__read", 1, 1)]);
        stats.Count(ChatToolOutcome.Succeeded).ShouldBe(2);
        stats.Count(ChatToolOutcome.Failed).ShouldBe(1);
    }

    [Fact]
    public void ShouldKeepAnEmptyLastTurnEmpty()
    {
        var stats = Calculate([User(), Calls(new ChatToolCall("a", "read", "{}")), User()], scope: ChatWidgetScope.LastTurn);
        stats.Calls.ShouldBe(0);
        stats.Turns.ShouldBe(1);
    }

    [Fact]
    public void ShouldMergeLiveCallsOnceAndPreferSavedResults()
    {
        var user = User();
        var run = Run(user) with { ActiveTools = [Active("a"), Active("b")] };
        var stats = Calculate([user, Calls(new ChatToolCall("a", "read", "{}"), new("c", "read", "{}")),
            Result("a", "{\"isError\":false}")], run, ChatWidgetScope.LastTurn);

        stats.Calls.ShouldBe(3);
        stats.IsRunning.ShouldBeTrue();
        stats.Count(ChatToolOutcome.Succeeded).ShouldBe(1);
        stats.Count(ChatToolOutcome.Running).ShouldBe(1);
        stats.Count(ChatToolOutcome.AwaitingResult).ShouldBe(1);
    }

    [Fact]
    public void ShouldIgnoreSnapshotsFromAnotherBranchOrEarlierTurn()
    {
        var earlier = User();
        foreach (var activeUser in new[] { earlier, User() })
        {
            var stats = Calculate([earlier, User()], Run(activeUser) with { ActiveTools = [Active("a")] });
            stats.Calls.ShouldBe(0);
            stats.IsRunning.ShouldBeFalse();
        }
    }

    [Fact]
    public void ShouldDistinguishMissingAndUnknownResults()
    {
        var stats = Calculate([User(), Calls(new ChatToolCall("a", "read", "{}"), new("b", "read", "{}"),
            new("c", "read", "{}"), new("d", "read", "{}")),
            Result("b", "legacy text"), Result("c", "{\"isError\":false}") with { ContentOmitted = true },
            Result("d", "{\"isError\":false}") with { IsIncomplete = true }]);
        stats.Count(ChatToolOutcome.NoResult).ShouldBe(1);
        stats.Count(ChatToolOutcome.Unknown).ShouldBe(3);
        stats.Count(ChatToolOutcome.Succeeded).ShouldBe(0);
    }

    [Fact]
    public void ShouldUseCompactResultFlagsWithoutLoadingOutput()
    {
        var stats = Calculate([User(), Calls(new ChatToolCall("a", "read", "{}"), new("b", "read", "{}")),
            Result("a", "") with { ContentOmitted = true, ToolResultIsError = false },
            Result("b", "") with { ContentOmitted = true, ToolResultIsError = true }]);
        stats.Count(ChatToolOutcome.Succeeded).ShouldBe(1);
        stats.Count(ChatToolOutcome.Failed).ShouldBe(1);
        stats.Count(ChatToolOutcome.Unknown).ShouldBe(0);
    }

    [Fact]
    public void ShouldMatchResultsWithinEachTurnAndDeduplicateCallIds()
    {
        var stats = Calculate([User(), Calls(new ChatToolCall("a", "read", "{}"), new("a", "read", "{}")),
            Result("a", "{\"isError\":false}"), User(), Calls(new ChatToolCall("a", "read", "{}"))]);
        stats.Calls.ShouldBe(2);
        stats.Count(ChatToolOutcome.Succeeded).ShouldBe(1);
        stats.Count(ChatToolOutcome.NoResult).ShouldBe(1);
    }

    [Fact]
    public void ShouldNotUseStaleActiveCallsAfterTheRunEnds()
    {
        var user = User();
        var stats = Calculate([user, Calls(new ChatToolCall("a", "read", "{}"))],
            Run(user) with { Status = ChatRunStatus.Completed, ActiveTools = [Active("a"), Active("b")] });
        stats.Calls.ShouldBe(1);
        stats.Count(ChatToolOutcome.NoResult).ShouldBe(1);
        stats.IsRunning.ShouldBeFalse();
    }

    private ChatToolStatistics Calculate(IReadOnlyList<ChatMessageView> messages, ChatRunSnapshot? run = null,
        ChatWidgetScope scope = ChatWidgetScope.Chat) => _calculator.Calculate(messages, run, scope);
    private static ChatMessageView User() => new(Guid.NewGuid(), null, "User", "Hi", DateTimeOffset.UnixEpoch);
    private static ChatMessageView Calls(params ChatToolCall[] calls) =>
        new(Guid.NewGuid(), null, "Assistant", "", DateTimeOffset.UnixEpoch, ToolCalls: calls);
    private static ChatMessageView Result(string id, string content) =>
        new(Guid.NewGuid(), null, "Tool", content, DateTimeOffset.UnixEpoch, ToolCallId: id);
    private static ActiveToolInvocation Active(string id) => new(id, "read", "{}", DateTimeOffset.UnixEpoch);
    private static ChatRunSnapshot Run(ChatMessageView user) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        ChatRunStatus.Generating, "", [], false, null, 1, ActiveMessageId: user.Id);
}
