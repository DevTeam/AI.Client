namespace AI.Client.Mcp.BuiltIn.Web;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class FetchTool(IWebFetcher fetcher) : IToolFactory
{
    private const int MaxLengthLimit = 1000000;

    public McpServerTool Create() => McpServerTool.Create(
        FetchAsync,
        new McpServerToolCreateOptions
        {
            Description = "Fetch an http or https URL and return its content as Markdown. HTML is reduced to headings, links, lists and text; "
                          + "pass 'raw' to get the response body unchanged. Non-HTML responses such as JSON or plain text are returned as is. "
                          + "When the result is truncated, call again with 'startIndex' set to the returned 'nextIndex' to continue. "
                          + "The request follows up to 5 redirects, times out after 30 seconds and reads at most 5 MiB. "
                          + "robots.txt is not consulted, so do not use this tool to crawl a site."
        });

    [McpServerTool(Name = "fetch", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = true,
        UseStructuredContent = true, OutputSchemaType = typeof(FetchResult))]
    private async Task<CallToolResult> FetchAsync(
        [Description("Absolute http or https URL to fetch.")] [MaxLength(2048)] string url,
        [Description("Maximum number of characters to return.")] [Range(1, MaxLengthLimit)] int maxLength = 20000,
        [Description("Return content starting at this character index of the extracted text.")] [Range(0, int.MaxValue)] int startIndex = 0,
        [Description("Return the response body without HTML simplification.")] bool raw = false,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var target)
            || (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps))
        {
            return ToolReply.Of(new FetchResult(url, 0, "", "", startIndex, startIndex, false, "URL must be an absolute http or https URL."), true);
        }

        if (!string.IsNullOrEmpty(target.UserInfo))
        {
            return ToolReply.Of(new FetchResult(url, 0, "", "", startIndex, startIndex, false, "URL must not carry credentials."), true);
        }

        var response = await fetcher.GetAsync(target, cancellationToken);
        if (response.Error is not null && response.Body.Length == 0)
        {
            return ToolReply.Of(new FetchResult(response.FinalUrl, response.Status, response.ContentType, "", startIndex, startIndex, false, response.Error), true);
        }

        var html = response.ContentType.Contains("html", StringComparison.OrdinalIgnoreCase)
                   || response.ContentType.Contains("xml", StringComparison.OrdinalIgnoreCase);
        var text = raw || !html ? response.Body : HtmlText.ToMarkdown(response.Body);
        var from = Math.Min(startIndex, text.Length);
        var window = text.AsSpan(from);
        var truncated = response.BodyTruncated || window.Length > maxLength;
        if (window.Length > maxLength)
        {
            window = window[..maxLength];
        }

        return ToolReply.Of(new FetchResult(
            response.FinalUrl,
            response.Status,
            response.ContentType,
            window.ToString(),
            from,
            from + window.Length,
            truncated,
            response.Error));
    }
}
