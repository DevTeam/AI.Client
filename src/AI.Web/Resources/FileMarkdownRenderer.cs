namespace AI.Web.Resources;

using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

public interface IFileMarkdownRenderer
{
    string Render(string markdown);
}

/// <summary>Local documents use a pipeline without raw HTML or arbitrary element attributes.</summary>
public sealed class FileMarkdownRenderer : IFileMarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseTaskLists().UseAutoLinks().DisableHtml().Build();

    public string Render(string markdown)
    {
        var document = Markdown.Parse(markdown, _pipeline);
        foreach (var link in document.Descendants<LinkInline>())
        {
            // Relative document links cannot be resolved against the application's own URL.
            if (link.Url is { } url && !IsSafeUrl(url, link.IsImage)) link.Url = "#";
            link.GetDynamicUrl = null;
        }
        foreach (var link in document.Descendants<AutolinkInline>())
            if (!link.IsEmail && !IsSafeUrl(link.Url, false)) link.Url = "#";
        return Markdown.ToHtml(document, _pipeline);
    }

    private static bool IsSafeUrl(string url, bool image) => url.StartsWith('#')
        || Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme is "https" or "http" || !image && uri.Scheme == "mailto");
}
