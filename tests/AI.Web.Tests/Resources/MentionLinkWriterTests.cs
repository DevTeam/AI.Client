namespace AI.Web.Tests.Resources;

using AI.Contracts.Resources;
using AI.Web.Markdown;
using AI.Web.Resources;
using Shouldly;
using Xunit;

public sealed class MentionLinkWriterTests
{
    private readonly MentionLinkWriter _writer = new();

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
        html.ShouldContain($"href=\"#mention-{chat.Id}\"");
        html.ShouldContain("class=\"mention-link mention-chat\"");
        html.ShouldContain(">@chat:&quot;Deploy fix&quot;</a>.");
    }

    [Fact]
    public void ShouldLeaveCodeAndLongerWordsAlone()
    {
        var folder = new ChatResourceRef(Guid.NewGuid(), ChatResourceKind.Directory, "C:\\repo\\src", Mention: "@src/");

        _writer.Link("`@src/` and @src/x.cs and me@src/", [folder]).ShouldBe("`@src/` and @src/x.cs and me@src/");
        _writer.Link("no links", [new ChatResourceRef(Guid.NewGuid(), ChatResourceKind.File, "C:\\x")]).ShouldBe("no links");
    }
}
