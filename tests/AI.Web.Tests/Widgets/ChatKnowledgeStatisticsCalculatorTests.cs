namespace AI.Web.Tests.Widgets;

using AI.Contracts.Chat;
using AI.Contracts.Chats;
using AI.Contracts.Runs;
using AI.Contracts.Tools;
using AI.Web.Widgets;
using Shouldly;
using Xunit;

public class ChatKnowledgeStatisticsCalculatorTests
{
    private readonly ChatKnowledgeStatisticsCalculator _calculator = new();

    [Fact]
    public void ShouldGroupReadFilesByToolAndKeepMostRecentPaths()
    {
        var first = Calls(ReadTextFile("src/old.cs"), ReadTextFile("src/keep.cs"));
        var second = Calls(ReadTextFile("src/new.cs"), SearchFiles("src/new.cs"));

        var stats = _calculator.Calculate([User(), first, User(), second], null, ChatWidgetScope.Chat);

        stats.TotalReads.ShouldBe(4);
        stats.TurnsWithReads.ShouldBe(2);
        stats.Groups.Count.ShouldBe(2);

        var fileGroup = stats.Groups.Single(group => group.ToolName == "read_text_file");
        fileGroup.Server.ShouldBe("Built-in");
        fileGroup.ToolLabel.ShouldBe("Read file");
        fileGroup.Calls.ShouldBe(3);
        fileGroup.Recent.Select(source => source.Display).ShouldBe(["src/new.cs", "src/keep.cs", "src/old.cs"]);
        fileGroup.Recent.Select(source => source.Occurrences).ShouldBe([1, 1, 1]);

        var searchGroup = stats.Groups.Single(group => group.ToolName == "search_files");
        searchGroup.Calls.ShouldBe(1);
        searchGroup.Recent.ShouldHaveSingleItem().Display.ShouldBe("src/new.cs");

        stats.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public void ShouldTreatSamePathReadTwiceAsASingleRecentEntry()
    {
        var stats = _calculator.Calculate(
            [User(), Calls(ReadTextFile("src/app.css"), ReadTextFile("src/app.css")), User(),
                Calls(ReadTextFile("src/app.css"))],
            null, ChatWidgetScope.Chat);

        var group = stats.Groups.Single();
        group.Calls.ShouldBe(3);
        group.Recent.ShouldHaveSingleItem();
        group.Recent[0].Display.ShouldBe("src/app.css");
        group.Recent[0].Occurrences.ShouldBe(3);
        stats.TotalReads.ShouldBe(3);
    }

    [Fact]
    public void ShouldLimitRecentSourcesToThree()
    {
        var calls = Calls(ReadTextFile("a.cs"), ReadTextFile("b.cs"), ReadTextFile("c.cs"), ReadTextFile("d.cs"));
        var stats = _calculator.Calculate([User(), calls], null, ChatWidgetScope.Chat);

        stats.Groups.Single().Recent.Select(source => source.Display).ShouldBe(["d.cs", "c.cs", "b.cs"]);
    }

    [Fact]
    public void ShouldCountReadMultipleFilesByExpandingTheArray()
    {
        var stats = _calculator.Calculate(
            [User(), Calls(ReadMultipleFiles(["src/a.cs", "src/b.cs", "src/a.cs"]))],
            null, ChatWidgetScope.Chat);

        stats.TotalReads.ShouldBe(3);
        var group = stats.Groups.Single(group => group.ToolName == "read_multiple_files");
        group.ToolLabel.ShouldBe("Read files");
        group.Calls.ShouldBe(3);
        // Recent shows newest first; both src/a.cs and src/b.cs were added in the same batch, so
        // the older half of the array ends up after the newer half when we reverse the tail.
        group.Recent.Select(source => source.Display).ShouldBe(["src/b.cs", "src/a.cs"]);
        group.Recent.Single(source => source.Display == "src/a.cs").Occurrences.ShouldBe(2);
    }

    [Fact]
    public void ShouldPickTheUrlFromFetchArguments()
    {
        var stats = _calculator.Calculate([User(), Calls(Fetch("https://example.com/docs"))], null, ChatWidgetScope.Chat);

        var group = stats.Groups.Single();
        group.ToolName.ShouldBe("fetch");
        group.ToolLabel.ShouldBe("Fetch page");
        group.Recent.ShouldHaveSingleItem().Display.ShouldBe("https://example.com/docs");
    }

    [Fact]
    public void ShouldNormaliseWindowsPathsToForwardSlashes()
    {
        var stats = _calculator.Calculate(
            [User(), Calls(new ChatToolCall("a", "read_text_file", "{\"path\":\"C:\\\\Projects\\\\app.cs\"}"))],
            null, ChatWidgetScope.Chat);

        stats.Groups.Single().Recent.ShouldHaveSingleItem().Display.ShouldBe("C:/Projects/app.cs");
    }

    [Fact]
    public void ShouldIgnoreNonReadToolsEvenWhenTheyAppearInTheBranch()
    {
        var stats = _calculator.Calculate(
            [User(), Calls(ReadTextFile("src/keep.cs"), new ChatToolCall("b", "write_file", "{\"path\":\"x.cs\"}"),
                new ChatToolCall("c", "edit_file", "{}"), new ChatToolCall("d", "process_run", "{\"command\":\"cat x\"}"))],
            null, ChatWidgetScope.Chat);

        stats.Groups.ShouldHaveSingleItem();
        stats.Groups[0].ToolName.ShouldBe("read_text_file");
        stats.TotalReads.ShouldBe(1);
    }

    [Fact]
    public void ShouldUseLastTurnScopeToCountOnlyTheFinalTurn()
    {
        var stats = _calculator.Calculate(
            [User(), Calls(ReadTextFile("old.cs")), User(), Calls(ReadTextFile("new.cs"))],
            null, ChatWidgetScope.LastTurn);

        stats.TotalReads.ShouldBe(1);
        stats.Groups.ShouldHaveSingleItem();
        stats.Groups[0].Recent.ShouldHaveSingleItem().Display.ShouldBe("new.cs");
    }

    [Fact]
    public void ShouldPickUpLiveReadsFromTheRunSnapshotOnlyForTheLastTurn()
    {
        var user = User();
        var run = Run(user) with { ActiveTools = [ActiveRead("read_text_file", "src/live.cs"), ActiveRead("search_files", "src/live.cs")] };

        var stats = _calculator.Calculate([user, Calls(ReadTextFile("old.cs"))], run, ChatWidgetScope.LastTurn);

        stats.TotalReads.ShouldBe(3);
        stats.IsRunning.ShouldBeTrue();
        stats.Groups.Select(group => group.ToolName).ShouldBe(["read_text_file", "search_files"]);
    }

    [Fact]
    public void ShouldIgnoreSnapshotsThatDoNotMatchTheVisibleTurn()
    {
        var stale = User();
        foreach (var mismatched in new[] { User(), stale })
        {
            var stats = _calculator.Calculate([stale, User()],
                Run(mismatched) with { ActiveTools = [ActiveRead("read_text_file", "src/x.cs")] },
                ChatWidgetScope.LastTurn);
            stats.TotalReads.ShouldBe(0);
            stats.IsRunning.ShouldBeFalse();
        }
    }

    [Fact]
    public void ShouldDropNonReadCallsFromTheLiveSnapshot()
    {
        var user = User();
        var run = Run(user) with
        {
            ActiveTools =
            [
                ActiveRead("read_text_file", "src/keep.cs"),
                ActiveRead("write_file", "src/extra.cs"),
                ActiveRead("process_run", "{\"command\":\"ls\"}")
            ]
        };

        var stats = _calculator.Calculate([user], run, ChatWidgetScope.LastTurn);

        stats.TotalReads.ShouldBe(1);
        stats.Groups.ShouldHaveSingleItem();
        stats.Groups[0].Recent.ShouldHaveSingleItem().Display.ShouldBe("src/keep.cs");
    }

    [Fact]
    public void ShouldCountMcpServerReadsAsTheirOwnServer()
    {
        var stats = _calculator.Calculate(
            [User(), Calls(new ChatToolCall("a", "mcp_fs__read_text_file", "{\"path\":\"src/from_mcp.cs\"}"))],
            null, ChatWidgetScope.Chat);

        var group = stats.Groups.ShouldHaveSingleItem();
        group.Server.ShouldBe("mcp_fs");
        group.ToolName.ShouldBe("read_text_file");
    }

    [Fact]
    public void ShouldReturnEmptyForAnEmptyBranch()
    {
        _calculator.Calculate([], null, ChatWidgetScope.Chat).ShouldBe(ChatKnowledgeStatistics.Empty);
    }

    private static ChatMessageView User() => new(Guid.NewGuid(), null, "User", "Hi", DateTimeOffset.UnixEpoch);

    private static ChatMessageView Calls(params ChatToolCall[] calls) =>
        new(Guid.NewGuid(), null, "Assistant", "", DateTimeOffset.UnixEpoch, ToolCalls: calls);

    private static ChatToolCall ReadTextFile(string path) =>
        new(Guid.NewGuid().ToString("n"), "read_text_file", "{\"path\":" + System.Text.Json.JsonSerializer.Serialize(path) + "}");

    private static ChatToolCall ReadMultipleFiles(IEnumerable<string> paths)
    {
        var array = string.Join(",", paths.Select(item => System.Text.Json.JsonSerializer.Serialize(item)));
        return new(Guid.NewGuid().ToString("n"), "read_multiple_files", "{\"paths\":[" + array + "]}");
    }

    private static ChatToolCall SearchFiles(string path) =>
        new(Guid.NewGuid().ToString("n"), "search_files", "{\"path\":" + System.Text.Json.JsonSerializer.Serialize(path) + "}");

    private static ChatToolCall Fetch(string url) =>
        new(Guid.NewGuid().ToString("n"), "fetch", "{\"url\":" + System.Text.Json.JsonSerializer.Serialize(url) + "}");

    private static ActiveToolInvocation ActiveRead(string name, string arg) =>
        new(Guid.NewGuid().ToString("n"), name, "{\"path\":" + System.Text.Json.JsonSerializer.Serialize(arg) + "}", DateTimeOffset.UnixEpoch);

    private static ChatRunSnapshot Run(ChatMessageView user) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ChatRunStatus.Generating, "", [], false, null, 1, ActiveMessageId: user.Id);
}
