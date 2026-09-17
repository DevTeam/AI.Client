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

    /// <summary>
    /// No advanced extensions here: the attribute syntax they add is what lets markdown hang an
    /// event handler on an element, and a label written by a model is not worth that surface.
    /// </summary>
    private readonly MarkdownPipeline _inline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .Build();

    public string Render(string markdown) => Markdown.ToHtml(markdown, _pipeline);

    public string RenderInline(string markdown)
    {
        // Folding the source onto one line is what makes this inline: a list or a fence needs its
        // own lines to be one, so without them the syntax stays the literal text it looks like.
        var single = string.Join(' ', (markdown ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var html = Markdown.ToHtml(single, _inline).Trim();
        return html.StartsWith("<p>", StringComparison.Ordinal) && html.EndsWith("</p>", StringComparison.Ordinal)
            ? html[3..^4]
            : html;
    }
}
