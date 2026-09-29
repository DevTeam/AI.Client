namespace AI.Web.Tests.Resources;

using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Resources;
using AI.Web.Resources;
using Shouldly;
using Xunit;

public sealed class ResourceMentionMatcherTests
{
    private readonly ResourceMentionMatcher _matcher = new();

    [Theory]
    [InlineData("@", 1, "")]
    [InlineData("look at @Hom", 12, "Hom")]
    [InlineData("look at @Hom and more", 10, "Hom")]
    [InlineData("name@example.com", 16, null)]
    [InlineData("@Home ", 6, null)]
    [InlineData("@chat:\"Deploy", 13, null)]
    [InlineData("plain", 5, null)]
    public void ShouldFindTheWordTheCaretIsIn(string text, int caret, string? query) =>
        _matcher.GetMention(text, caret)?.Query.ShouldBe(query);

    [Fact]
    public void ShouldReplaceTheWholeWordEvenWithTheCaretInside()
    {
        var mention = _matcher.GetMention("see @Home.razor now", 8).ShouldNotBeNull();
        mention.Start.ShouldBe(4);
        mention.End.ShouldBe(15);
    }

    [Fact]
    public void ShouldReadScopesAndLineRanges()
    {
        var scoped = _matcher.GetMention("@chat:deploy", 12).ShouldNotBeNull();
        scoped.Scope.ShouldBe(ChatResourceKind.Chat);
        scoped.Filter.ShouldBe("deploy");

        var lines = _matcher.GetMention("@Home.razor:140-120", 19).ShouldNotBeNull();
        lines.Scope.ShouldBe(ChatResourceKind.File);
        lines.Filter.ShouldBe("Home.razor");
        lines.Lines.ShouldBe(new ChatLineRange(120, 140));

        _matcher.GetMention("@chat.cs", 8).ShouldNotBeNull().Scope.ShouldBeNull();
    }

    [Fact]
    public void ShouldOfferEverySourceAsALinkAndBrowseLast()
    {
        var currentChat = Guid.NewGuid();
        var otherChat = Guid.NewGuid();
        var project = Guid.NewGuid();
        var sources = new ResourceMentionSources(
            [new ResourceSuggestion(ChatResourceKind.File, "C:\\repo\\src\\Home.razor", "src/Home.razor", PathAccess.Read)],
            [new WorkspaceDiffSource("C:\\repo", "repo", 3)],
            [Chat(currentChat, "Home work"), Chat(otherChat, "Home page fix")],
            [],
            [new ProjectSummary(project, "Homepage site", "", DateTimeOffset.UnixEpoch, 1)],
            [],
            currentChat,
            Guid.NewGuid());

        var items = _matcher.Match(sources, _matcher.GetMention("@Home", 5)!);

        items.Select(item => item.Token).ShouldBe(["@src/Home.razor", "@chat:\"Home page fix\"", "@project:\"Homepage site\"", "", ""]);
        items[0].Highlights.ShouldBe([0, 1, 2, 3]);
        items[^1].Action.ShouldBe(ResourceMentionAction.BrowseDirectory);
    }

    [Fact]
    public void ShouldOfferChangesForTheDiffWordAndLinesOnlyForFiles()
    {
        var sources = new ResourceMentionSources(
            [new ResourceSuggestion(ChatResourceKind.Directory, "C:\\repo\\src", "src", PathAccess.Read),
                new ResourceSuggestion(ChatResourceKind.File, "C:\\repo\\src\\a b.cs", "src/a b.cs", PathAccess.Read)],
            [new WorkspaceDiffSource("C:\\repo", "repo", 1, "repo"),
                new WorkspaceDiffSource("C:\\work\\tools", "tools", 2, "work/tools")], [], [], [], [], null, null);

        var diff = _matcher.Match(sources, _matcher.GetMention("@diff", 5)!);
        diff[0].Token.ShouldBe("@diff:repo");
        diff[0].Detail.ShouldBe("1 changed file");

        var lines = _matcher.Match(sources, _matcher.GetMention("@a:3-4", 6)!);
        lines.ShouldHaveSingleItem().Token.ShouldBe("@\"src/a b.cs\":3-4");
        lines[0].Name.ShouldBe("a b.cs:3-4");
    }

    private static ChatSummary Chat(Guid id, string title) =>
        new(id, Guid.NewGuid(), title, DateTimeOffset.UnixEpoch, 1, DateTimeOffset.UnixEpoch);
}
