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

    [Fact]
    public void ShouldOfferTheProjectDirectoriesFirstBeforeTheHostAnswers()
    {
        var sources = new ResourceMentionSources([], [], [], [], [], [], null, null,
            [new ProjectDirectory("App", "C:\\work\\App"), new ProjectDirectory("Docs", "C:\\work\\Docs")]);

        var all = _matcher.Match(sources, _matcher.GetMention("@", 1)!);
        var narrowed = _matcher.Match(sources, _matcher.GetMention("@ap", 3)!);
        var path = _matcher.Match(sources, _matcher.GetMention("@App/", 5)!);

        all.Take(2).Select(item => (item.Group, item.Token, item.OpenAs))
            .ShouldBe([(ResourceMentionGroup.Directories, "@App/", "App"), (ResourceMentionGroup.Directories, "@Docs/", "Docs")]);
        narrowed[0].Name.ShouldBe("App");
        narrowed.Count(item => item.Group == ResourceMentionGroup.Directories).ShouldBe(1);
        // Inside a directory, its contents come from the Host instead.
        path.ShouldNotContain(item => item.Group == ResourceMentionGroup.Directories);
    }

    [Fact]
    public void ShouldListADirectoryWholeAndNarrowItByTheNameAfterTheSlash()
    {
        var files = Enumerable.Range(0, 12)
            .Select(index => new ResourceSuggestion(ChatResourceKind.Directory, $"C:\\App\\dir{index:00}", $"App/dir{index:00}", PathAccess.Read))
            .Append(new ResourceSuggestion(ChatResourceKind.File, "C:\\App\\Home.razor", "App/Home.razor", PathAccess.Read))
            .ToArray();
        var sources = new ResourceMentionSources(files, [], [], [], [], [], null, null, FilesTotal: 13);

        var mention = _matcher.GetMention("@App/", 5)!;
        var listed = _matcher.Match(sources, mention);
        var narrowed = _matcher.Match(sources, _matcher.GetMention("@App/hrz", 8)!);

        _matcher.Folder(mention).ShouldBe("App/");
        listed.Count(item => item.Group == ResourceMentionGroup.Files).ShouldBe(13);
        // The path above the list names the directory; the rows do not repeat it.
        listed.Where(item => item.Group == ResourceMentionGroup.Files).ShouldAllBe(item => item.Detail == null);
        listed.ShouldNotContain(item => item.Action == ResourceMentionAction.ShowMore);
        narrowed[0].Token.ShouldBe("@App/Home.razor");
        narrowed[0].Highlights.ShouldBe([0, 5, 7]);
        _matcher.Folder(_matcher.GetMention("@App/Home.razor:3", 17)!).ShouldBeNull();
    }

    [Fact]
    public void ShouldCountTheRowsASearchLeftOutAndShowThemOnRequest()
    {
        var files = Enumerable.Range(0, 20)
            .Select(index => new ResourceSuggestion(ChatResourceKind.File, $"C:\\App\\page{index:00}.cs", $"page{index:00}.cs", PathAccess.Read))
            .ToArray();
        var mention = _matcher.GetMention("@page", 5)!;

        var limited = _matcher.Match(new ResourceMentionSources(files, [], [], [], [], [], null, null, FilesTotal: 70), mention);
        var whole = _matcher.Match(new ResourceMentionSources(files, [], [], [], [], [], null, null, FilesTotal: 70,
            Expanded: [ResourceMentionGroup.Files]), mention);

        var more = limited.Single(item => item.Action == ResourceMentionAction.ShowMore);
        more.Name.ShouldBe("62 more");
        more.Expandable.ShouldBeTrue();
        whole.Count(item => item is { Group: ResourceMentionGroup.Files, Action: ResourceMentionAction.Attach }).ShouldBe(20);
        // What the Host never sent can only be counted, and narrowed by typing.
        whole.Single(item => item.Action == ResourceMentionAction.ShowMore).Expandable.ShouldBeFalse();
    }

    [Fact]
    public void ShouldCountWhatAPartlyListedDirectoryLeftOut()
    {
        var sources = new ResourceMentionSources(
            [new ResourceSuggestion(ChatResourceKind.File, "C:\\App\\a.cs", "App/a.cs", PathAccess.Read)],
            [], [], [], [], [], null, null, FilesTotal: 400);

        var listed = _matcher.Match(sources, _matcher.GetMention("@App/", 5)!);

        listed.Single(item => item.Action == ResourceMentionAction.ShowMore).ShouldSatisfyAllConditions(
            more => more.Name.ShouldBe("399 more"),
            more => more.Expandable.ShouldBeFalse());
    }

    private static ChatSummary Chat(Guid id, string title) =>
        new(id, Guid.NewGuid(), title, DateTimeOffset.UnixEpoch, 1, DateTimeOffset.UnixEpoch);
}
