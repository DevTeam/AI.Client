namespace AI.Web.Tests.Widgets;

using AI.Contracts.Memory;
using AI.Web.Widgets;
using Shouldly;
using Xunit;

public sealed class ChatMemoryStatisticsCalculatorTests
{
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly Guid OtherProject = Guid.NewGuid();
    private static readonly Guid Chat = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly ChatMemoryStatisticsCalculator _calculator = new();

    [Fact]
    public void ShouldCountThePersonsEntriesAndTheProjectsButNoOtherProject()
    {
        var stats = _calculator.Calculate([
            Entry(MemoryScope.User, null, MemoryKind.Profile, "Speaks Russian"),
            Entry(MemoryScope.Project, Project, MemoryKind.Fact, "Uses Pure.DI"),
            Entry(MemoryScope.Project, OtherProject, MemoryKind.Fact, "Another project's entry")
        ], Project, Chat);

        stats.Total.ShouldBe(2);
        stats.UserEntries.ShouldBe(1);
        stats.ProjectEntries.ShouldBe(1);
        stats.HasProject.ShouldBeTrue();
        stats.Entries.ShouldNotContain(entry => entry.Title == "Another project's entry");
    }

    [Fact]
    public void ShouldLeaveOutDisabledEntriesBecauseTheyReachNoRun()
    {
        var stats = _calculator.Calculate([
            Entry(MemoryScope.User, null, MemoryKind.Preference, "Answer briefly"),
            Entry(MemoryScope.User, null, MemoryKind.Preference, "Switched off", enabled: false)
        ], Project, Chat);

        stats.Total.ShouldBe(1);
        stats.Entries.ShouldHaveSingleItem().Title.ShouldBe("Answer briefly");
    }

    [Fact]
    public void ShouldOrderPinnedThenSavedHereThenNewest()
    {
        var newer = Entry(MemoryScope.User, null, MemoryKind.Fact, "Newer", updated: T0.AddHours(2));
        var older = Entry(MemoryScope.User, null, MemoryKind.Fact, "Older", updated: T0);
        var pinned = Entry(MemoryScope.Project, Project, MemoryKind.Preference, "Pinned", pinned: true, updated: T0.AddHours(-5));
        var here = Entry(MemoryScope.Project, Project, MemoryKind.Fact, "Saved here", updated: T0, chatId: Chat);

        var stats = _calculator.Calculate([older, newer, pinned, here], Project, Chat);

        stats.Entries.Select(entry => entry.Title)
            .ShouldBe(["Pinned", "Saved here", "Newer", "Older"]);
        stats.FromThisChat.ShouldBe(1);
        stats.Pinned.ShouldBe(1);
    }

    [Fact]
    public void ShouldNotMarkAnEntrySavedInAnotherChatAsSavedHere()
    {
        var stats = _calculator.Calculate([
            Entry(MemoryScope.Project, Project, MemoryKind.Fact, "Elsewhere", chatId: Guid.NewGuid())
        ], Project, Chat);

        stats.FromThisChat.ShouldBe(0);
        stats.Entries.ShouldHaveSingleItem().IsFromChat(Chat).ShouldBeFalse();
    }

    [Fact]
    public void ShouldCountKindsAndSayThatAChatWithoutAProjectHasNoneOfTheProject()
    {
        var stats = _calculator.Calculate([
            Entry(MemoryScope.User, null, MemoryKind.Profile, "Person"),
            Entry(MemoryScope.User, null, MemoryKind.Preference, "Style"),
            Entry(MemoryScope.User, null, MemoryKind.Preference, "Commands"),
            Entry(MemoryScope.Project, Project, MemoryKind.Fact, "Project fact")
        ], null, Chat);

        stats.Total.ShouldBe(3);
        stats.Count(MemoryKind.Profile).ShouldBe(1);
        stats.Count(MemoryKind.Preference).ShouldBe(2);
        stats.Count(MemoryKind.Fact).ShouldBe(0);
        stats.HasProject.ShouldBeFalse();
        stats.ProjectEntries.ShouldBe(0);
    }

    [Fact]
    public void ShouldCountAnEntryOnceWhenTheCatalogHoldsItTwice()
    {
        var entry = Entry(MemoryScope.User, null, MemoryKind.Fact, "Once");

        var stats = _calculator.Calculate([entry, entry], Project, Chat);

        stats.Total.ShouldBe(1);
    }

    [Fact]
    public void ShouldBeEmptyWithoutEntries() =>
        _calculator.Calculate([], Project, Chat).ShouldBe(new ChatMemoryStatistics([], 0, true));

    private static MemoryEntry Entry(MemoryScope scope, Guid? projectId, MemoryKind kind, string title,
        bool pinned = false, bool enabled = true, Guid? chatId = null, DateTimeOffset? updated = null) =>
        new(Guid.NewGuid(), scope, projectId, kind, title, $"{title} body", [], pinned, enabled,
            chatId is null ? MemoryAuthor.User : MemoryAuthor.Model, chatId, T0, updated ?? T0, 1);
}
