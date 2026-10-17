namespace AI.Web.Tests.Markdown;

using AI.Web.Markdown;
using Shouldly;
using Xunit;

public sealed class MarkdownLineBreakTests
{
    [Fact]
    public void UserMessageKeepsEachLineBreak()
    {
        var html = new SafeMarkdownRenderer().RenderUser("first line\nsecond line");

        html.ShouldContain("first line<br />");
        html.ShouldContain("second line");
    }

    [Fact]
    public void ModelReplyFoldsASingleLineBreak()
    {
        var html = new SafeMarkdownRenderer().Render("first line\nsecond line");

        html.ShouldNotContain("<br");
    }
}
