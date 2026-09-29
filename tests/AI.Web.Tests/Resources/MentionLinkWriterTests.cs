namespace AI.Web.Tests.Resources;

using AI.Contracts.Resources;
using AI.Web.Markdown;
using AI.Web.Resources;
using Shouldly;
using Xunit;

public sealed class MentionLinkWriterTests
{
    private readonly MentionLinkWriter _writer = new(new ResourcePresenter(new DiffSnapshotReader()));

    [Fact]
    public void ShouldLinkFilesAsLocalPathsAndTheRestByReference()
    {
        var chat = new ChatResourceRef(Guid.NewGuid(), ChatResourceKind.Chat, Guid.NewGuid().ToString(), "Deploy fix",
            Mention: "@chat:\"Deploy fix\"");
        var file = new ChatResourceRef(Guid.NewGuid(), ChatResourceKind.File, "C:\\repo\\my app.cs",
            Lines: new ChatLineRange(2, 4), Mention: "@\"my app.cs\":2-4");

        var html = new SafeMarkdownRenderer().Render(_writer.Link("Compare @\"my app.cs\":2-4 with @chat:\"Deploy fix\".", [chat, file]));

        html.ShouldContain("href=\"file:///C:/repo/my%20app.cs#L2-L4\"");
        html.ShouldContain("class=\"mention-link mention-file\"");
        html.ShouldContain(">my app.cs:2-4</a>");
        html.ShouldContain($"href=\"#mention-{chat.Id}\"");
        html.ShouldContain("class=\"mention-link mention-chat\"");
        html.ShouldContain(">Deploy fix</a>.");
    }

    [Fact]
    public void ShouldKeepTheTokenInTheTitleOnOneMarkdownLine()
    {
        var file = new ChatResourceRef(Guid.NewGuid(), ChatResourceKind.File, "C:\\repo\\a&b \"x\".cs", Mention: "@a.cs");

        var markdown = _writer.Link("| @a.cs |", [file]);
        var html = new SafeMarkdownRenderer().Render(markdown);

        markdown.ShouldNotContain("\n");
        html.ShouldContain("title=\"@a.cs\nC:\\repo\\a&amp;b &quot;x&quot;.cs\"");
    }

    [Fact]
    public void ShouldShowTheCapturedTotalsOfADiff()
    {
        var diff = new ChatResourceRef(Guid.NewGuid(), ChatResourceKind.Diff, "C:\\repo", "repo",
            Excerpt: "diff --git a/x.cs b/x.cs\n--- a/x.cs\n+++ b/x.cs\n@@ -1,2 +1,2 @@\n-old\n+new\n+more\n", Mention: "@diff:repo");

        var html = new SafeMarkdownRenderer().Render(_writer.Link("@diff:repo?", [diff]));

        html.ShouldContain("class=\"mention-link mention-diff\"");
        html.ShouldContain("data-summary=\"+2 −1\"");
        html.ShouldContain(">repo</a>?");
    }

    [Fact]
    public void ShouldLeaveCodeAndLongerWordsAlone()
    {
        var folder = new ChatResourceRef(Guid.NewGuid(), ChatResourceKind.Directory, "C:\\repo\\src", Mention: "@src/");

        _writer.Link("`@src/` and @src/x.cs and me@src/", [folder]).ShouldBe("`@src/` and @src/x.cs and me@src/");
        _writer.Link("no links", [new ChatResourceRef(Guid.NewGuid(), ChatResourceKind.File, "C:\\x")]).ShouldBe("no links");
    }
}
