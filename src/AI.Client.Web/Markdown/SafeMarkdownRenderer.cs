namespace AI.Client.Web.Markdown;

using Ganss.Xss;
using Markdig;

public sealed class SafeMarkdownRenderer : IMarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private readonly HtmlSanitizer _sanitizer = new();

    public string Render(string markdown) =>
        _sanitizer.Sanitize(Markdown.ToHtml(markdown, _pipeline));
}
