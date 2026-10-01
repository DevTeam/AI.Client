namespace AI.Web.Tests.Widgets;

using AI.Contracts.Chats;
using AI.Contracts.Workspace;
using AI.Web.Components;
using AI.Web.Widgets;
using Moq;
using Shouldly;
using Xunit;

public class ChatFileStatisticsCalculatorTests
{
    private readonly Mock<IChatFeedProjection> _feed = new();
    private readonly ChatFileStatisticsCalculator _calculator;
    private DateTimeOffset _clock = DateTimeOffset.UnixEpoch;

    public ChatFileStatisticsCalculatorTests() => _calculator = new ChatFileStatisticsCalculator(_feed.Object);

    [Fact]
    public void ShouldSumEditsOfOneFileAcrossTurns()
    {
        var first = Answer(Change("src/app.css", FileChangeKind.Modified, 10, 0));
        var second = Answer(Change("src/app.css", FileChangeKind.Modified, 0, 4), Change("README.md", FileChangeKind.Added, 3, 0));

        var stats = _calculator.Calculate([User(), first, User(), User(), second], null, ChatWidgetScope.Chat);

        stats.Files.Select(file => (file.Path, file.Additions, file.Deletions, file.LatestSourceMessageId))
            .ShouldBe([("src/app.css", 10, 4, second.Id), ("README.md", 3, 0, second.Id)]);
        (stats.Additions, stats.Deletions, stats.Turns, stats.TurnsWithChanges, stats.Sources).ShouldBe((13, 4, 3, 2, 2));
        stats.LatestSourceMessageId.ShouldBe(second.Id);
    }

    [Fact]
    public void ShouldCountOnlyTheLastTurnWhenAsked()
    {
        var stats = _calculator.Calculate(
            [User(), Answer(Change("a.cs", FileChangeKind.Modified, 5, 1)), User(), Answer(Change("b.cs", FileChangeKind.Modified, 2, 2))],
            null, ChatWidgetScope.LastTurn);

        stats.Files.Select(file => file.Path).ShouldBe(["b.cs"]);
        stats.Turns.ShouldBe(1);
    }

    [Fact]
    public void ShouldKeepTheKindAFileEndedWith()
    {
        var stats = _calculator.Calculate(
        [
            User(), Answer(Change("new.cs", FileChangeKind.Added, 8, 0), Change("old.cs", FileChangeKind.Modified, 1, 1)),
            User(), Answer(Change("new.cs", FileChangeKind.Modified, 2, 1), Change("old.cs", FileChangeKind.Deleted, 0, 9))
        ], null, ChatWidgetScope.Chat);

        stats.Files.ToDictionary(file => file.Path, file => file.Kind)
            .ShouldBe(new Dictionary<string, FileChangeKind> { ["new.cs"] = FileChangeKind.Added, ["old.cs"] = FileChangeKind.Deleted });
    }

    [Fact]
    public void ShouldCarryAFileHistoryOverItsRename()
    {
        var stats = _calculator.Calculate(
        [
            User(), Answer(Change("a.cs", FileChangeKind.Modified, 4, 0)),
            User(), Answer(Change("b.cs", FileChangeKind.Renamed, 1, 1, previousPath: "a.cs"))
        ], null, ChatWidgetScope.Chat);

        var file = stats.Files.ShouldHaveSingleItem();
        (file.Path, file.Kind, file.PreviousPath, file.Additions, file.Deletions).ShouldBe(("b.cs", FileChangeKind.Renamed, "a.cs", 5, 1));
    }

    [Fact]
    public void ShouldAddTheRunningTurnUntilItsReceiptArrives()
    {
        IReadOnlyList<ChatMessageView> branch = [User(), Answer(Change("a.cs", FileChangeKind.Modified, 1, 0)), User()];
        var live = Changes(Change("a.cs", FileChangeKind.Modified, 2, 0));

        var running = _calculator.Calculate(branch, live, ChatWidgetScope.Chat);
        _feed.Setup(item => item.LastTurnHasWorkspaceReceipt(branch)).Returns(true);
        var saved = _calculator.Calculate(branch, live, ChatWidgetScope.Chat);

        (running.IncludesLive, running.Additions, running.TurnsWithChanges).ShouldBe((true, 3, 2));
        (saved.IncludesLive, saved.Additions).ShouldBe((false, 1));
    }

    [Fact]
    public void ShouldLeaveAFileOnlyChangedByTheRunningTurnWithoutAReview()
    {
        var stats = _calculator.Calculate([User()], Changes(Change("a.cs", FileChangeKind.Added, 2, 0)), ChatWidgetScope.LastTurn);

        stats.Files.ShouldHaveSingleItem().LatestSourceMessageId.ShouldBeNull();
        stats.LatestSourceMessageId.ShouldBeNull();
    }

    [Fact]
    public void ShouldPutBinaryFilesLastAndMarkUnmeasuredOnes()
    {
        var stats = _calculator.Calculate(
        [
            User(), Answer(
                Change("logo.png", FileChangeKind.Modified, null, null, binary: true),
                Change("big.json", FileChangeKind.Modified, null, null, confidence: FileChangeConfidence.Approximate),
                Change("a.cs", FileChangeKind.Modified, 1, 0))
        ], null, ChatWidgetScope.Chat);

        stats.Files.Select(file => (file.Path, file.IsApproximate))
            .ShouldBe([("a.cs", false), ("big.json", true), ("logo.png", false)]);
    }

    private ChatMessageView User() => Message("User", null);

    private ChatMessageView Answer(params FileChange[] files) => Message("Assistant", Changes(files));

    private ChatMessageView Message(string role, WorkspaceChangeSet? changes)
    {
        _clock = _clock.AddMinutes(1);
        return new ChatMessageView(Guid.NewGuid(), null, role, "text", _clock, WorkspaceChanges: changes);
    }

    private static WorkspaceChangeSet Changes(params FileChange[] files) =>
        new(files, files.Sum(file => file.Additions ?? 0), files.Sum(file => file.Deletions ?? 0));

    private static FileChange Change(string path, FileChangeKind kind, int? additions, int? deletions,
        string? previousPath = null, bool binary = false, FileChangeConfidence confidence = FileChangeConfidence.Measured) =>
        new(path, kind, additions, deletions, previousPath, IsBinary: binary, Confidence: confidence);
}
