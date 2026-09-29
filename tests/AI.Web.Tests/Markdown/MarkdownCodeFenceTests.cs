namespace AI.Web.Tests.Markdown;

using AI.Web.Markdown;
using Shouldly;
using Xunit;

public sealed class MarkdownCodeFenceTests
{
    [Fact]
    public void LanguageFenceKeepsItsLanguageOnTheCodeElement()
    {
        var html = new SafeMarkdownRenderer().Render("```csharp\nvar value = 1;\n```");

        html.ShouldContain("<pre><code class=\"language-csharp\">var value = 1;");
    }
}
