namespace AI.Web.Tests.Widgets;

using AI.Contracts.Chats;
using AI.Contracts.Resources;
using AI.Web.Widgets;
using Shouldly;
using Xunit;

public class ChatReferenceStatisticsCalculatorTests
{
    private readonly ChatReferenceStatisticsCalculator _calculator = new();
    private DateTimeOffset _clock = DateTimeOffset.UnixEpoch;

    [Fact]
    public void ShouldAddOneTargetNamedInSeveralMessages()
    {
        var first = User(File("src/app.cs", "app.cs"));
        var second = Assistant(File("src/app.cs", "app.cs"));

        var stats = _calculator.Calculate([first, User(), second], isRunning: false, ChatWidgetScope.Chat);

        var entry = stats.References.ShouldHaveSingleItem();
        entry.Mentions.ShouldBe(2);
        entry.FirstMessageId.ShouldBe(first.Id);
        entry.FirstAt.ShouldBe(first.CreatedAt);
        stats.MessagesWithReferences.ShouldBe(2);
    }

    [Fact]
    public void ShouldKeepDifferentLineRangesOfOneFileApart()
    {
        var whole = File("src/app.cs", "app.cs");
        var part = File("src/app.cs", "app.cs") with { Lines = new ChatLineRange(12, 40) };

        var stats = _calculator.Calculate([User(whole), Assistant(part)], isRunning: false, ChatWidgetScope.Chat);

        stats.References.Select(entry => entry.Reference.Lines).ShouldBe([null, new ChatLineRange(12, 40)]);
    }

    [Fact]
    public void ShouldMatchUploadedFilesByTheirAssetId()
    {
        var uploaded = File("C:/temp/report.pdf", "report.pdf") with
        {
            AssetId = "asset-1", Source = ChatResourceSource.Upload
        };
        var sameBytes = File("C:/temp/copy.pdf", "copy.pdf") with
        {
            AssetId = "asset-1", Source = ChatResourceSource.Upload
        };

        var stats = _calculator.Calculate([User(uploaded), Assistant(sameBytes)], isRunning: false, ChatWidgetScope.Chat);

        stats.References.ShouldHaveSingleItem().Mentions.ShouldBe(2);
    }

    [Fact]
    public void ShouldCountOnlyTheLastTurnWhenAsked()
    {
        var stats = _calculator.Calculate(
        [
            User(File("old.cs", "old.cs")), Assistant(), User(), Assistant(File("new.cs", "new.cs"))
        ], isRunning: false, ChatWidgetScope.LastTurn);

        stats.References.ShouldHaveSingleItem().Reference.Path.ShouldBe("new.cs");
        stats.Turns.ShouldBe(1);
        stats.TurnsWithReferences.ShouldBe(1);
    }

    [Fact]
    public void ShouldIgnoreMessagesWithoutReferences()
    {
        var stats = _calculator.Calculate([User(), Assistant()], isRunning: false, ChatWidgetScope.Chat);

        stats.References.ShouldBeEmpty();
        stats.MessagesWithReferences.ShouldBe(0);
        stats.TurnsWithReferences.ShouldBe(0);
        stats.Turns.ShouldBe(1);
    }

    [Fact]
    public void ShouldCountTurnsWithAReference()
    {
        var stats = _calculator.Calculate(
        [
            User(File("a.cs", "a.cs")), Assistant(), User(), Assistant(), User(), Assistant(File("b.cs", "b.cs"))
        ], isRunning: false, ChatWidgetScope.Chat);

        stats.Turns.ShouldBe(3);
        stats.TurnsWithReferences.ShouldBe(2);
        stats.MessagesWithReferences.ShouldBe(2);
    }

    [Fact]
    public void ShouldOrderByMentionsThenKindThenPath()
    {
        var once = File("z.cs", "z.cs");
        var twice = File("a.cs", "a.cs");

        var stats = _calculator.Calculate(
        [User(once, twice), Assistant(twice), User(Directory("a-dir"))], isRunning: false, ChatWidgetScope.Chat);

        stats.References.Select(entry => (entry.Reference.Path, entry.Mentions))
            .ShouldBe([("a.cs", 2), ("z.cs", 1), ("a-dir", 1)]);
    }

    [Fact]
    public void ShouldSaySoFarOnlyForARunningLastTurn()
    {
        IReadOnlyList<ChatMessageView> branch = [User(File("a.cs", "a.cs")), Assistant()];

        _calculator.Calculate(branch, isRunning: true, ChatWidgetScope.Chat).IsRunning.ShouldBeFalse();
        _calculator.Calculate(branch, isRunning: true, ChatWidgetScope.LastTurn).IsRunning.ShouldBeTrue();
        _calculator.Calculate(branch, isRunning: false, ChatWidgetScope.LastTurn).IsRunning.ShouldBeFalse();
    }

    [Fact]
    public void ShouldReportAnEmptyBranch()
    {
        var stats = _calculator.Calculate([], isRunning: false, ChatWidgetScope.LastTurn);

        stats.ShouldBe(ChatReferenceStatistics.Empty);
    }

    private ChatMessageView User(params ChatResource[] resources) => Message("User", resources);

    private ChatMessageView Assistant(params ChatResource[] resources) => Message("Assistant", resources);

    private ChatMessageView Message(string role, params ChatResource[] resources)
    {
        _clock = _clock.AddMinutes(1);
        return new ChatMessageView(Guid.NewGuid(), null, role, "text", _clock,
            Resources: resources.Length == 0 ? null : resources);
    }

    private static ChatResource File(string path, string name) =>
        new(Guid.NewGuid(), ChatResourceKind.File, path, name);

    private static ChatResource Directory(string path) =>
        new(Guid.NewGuid(), ChatResourceKind.Directory, path);
}
