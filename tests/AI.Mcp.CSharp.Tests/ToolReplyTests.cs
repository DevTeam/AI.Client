using ModelContextProtocol.Protocol;
using Shouldly;
using Xunit;

namespace AI.Mcp.CSharp.Tests;

/// <summary>The checks that need no server process.</summary>
public sealed class ToolReplyTests
{
    [Fact]
    public void WritesStructuredContentAndMatchingText()
    {
        var result = new ToolReply().Reply(new { name = "тест", value = 2 });

        result.IsError.ShouldBe(false);
        result.StructuredContent.ShouldNotBeNull();
        result.StructuredContent!.Value.GetProperty("value").GetInt32().ShouldBe(2);
        var text = result.Content.ShouldHaveSingleItem().ShouldBeOfType<TextContentBlock>();
        text.Text.ShouldBe(result.StructuredContent.Value.GetRawText());
        // Non-Latin text must stay readable rather than becoming \uXXXX escapes.
        text.Text.ShouldContain("тест");
    }

    [Fact]
    public void MarksAFailedResultAsAnError()
    {
        var result = new ToolReply().Reply(new { success = false, error = "boom" }, isError: true);

        result.IsError.ShouldBe(true);
        result.StructuredContent!.Value.GetProperty("error").GetString().ShouldBe("boom");
    }
}
