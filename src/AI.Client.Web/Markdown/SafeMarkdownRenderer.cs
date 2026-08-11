using Ganss.Xss;
using Markdig;

namespace AI.Client.Web.Markdown;

public sealed class SafeMarkdownRenderer : IMarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private readonly HtmlSanitizer _sanitizer = new();

    public string Render(string markdown) =>
        _sanitizer.Sanitize(Markdig.Markdown.ToHtml(markdown ?? string.Empty, _pipeline));
}
