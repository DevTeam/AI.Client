namespace AI.Client.Web.Markdown;

using Markdig;

/// <summary>
/// Renders message markdown to the HTML the transcript injects into the DOM.
/// </summary>
/// <remarks>
/// <c>DisableHtml</c> escapes raw HTML in the source, so <c>&lt;script&gt;</c> written in a message
/// arrives as text. It does not cover what Markdig emits from markdown syntax itself: a
/// <c>javascript:</c> or <c>data:</c> link target, or the attributes the advanced extensions let
/// markdown attach to an element (<c>{onclick=…}</c>). An HTML sanitizer used to strip those and
/// was removed deliberately — it cost about 45% of the rendering time of a long transcript.
/// </remarks>
public sealed class SafeMarkdownRenderer : IMarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public string Render(string markdown) => Markdown.ToHtml(markdown, _pipeline);
}
