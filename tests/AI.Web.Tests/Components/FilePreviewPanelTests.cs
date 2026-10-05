namespace AI.Web.Tests.Components;

using System.Net;
using System.Net.Http.Json;
using AI.Contracts.Resources;
using AI.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using Shouldly;
using Xunit;

public sealed class FilePreviewPanelTests
{
    [Theory]
    [InlineData("image", "<img")]
    [InlineData("video", "<video")]
    [InlineData("audio", "<audio")]
    [InlineData("pdf", "<iframe")]
    [InlineData("binary", "This format cannot be previewed")]
    [InlineData("directory", "No readable files")]
    public async Task RendersEachFileTypeInTheSharedDrawer(string kind, string expected)
    {
        var file = new FilePreview("/workspace/sample", "sample", kind, "test/type", 42,
            kind == "directory" ? null : "file-preview-content/ticket", []);
        var html = await RenderAsync(file);
        html.ShouldContain("review-workspace file-preview-panel");
        html.ShouldContain(expected);
        html.ShouldContain("Close preview");
        if (kind is "video" or "audio") html.ShouldContain("preload=\"metadata\"");
        if (kind != "directory") html.ShouldContain("Download");
    }

    [Fact]
    public async Task EscapesTextAndShowsLineNumbersAndPaging()
    {
        var file = new FilePreview("/workspace/sample.html", "sample.html", "text", "text/html", 100000,
            "file-preview-content/ticket", []);
        var html = await RenderAsync(file, new FilePreviewText("<script>alert('test')</script>\nsecond line", 32768));
        html.ShouldNotContain("<script>");
        html.ShouldContain("&lt;script&gt;");
        html.ShouldContain("file-preview-line-numbers");
        html.ShouldContain("Load more text");
    }

    [Fact]
    public async Task ShowsAccessErrorsInsteadOfAnEmptyViewer()
    {
        var file = new FilePreview("/workspace/secret", "secret", "text", "text/plain", 0, null, []);
        var html = await RenderAsync(file, status: HttpStatusCode.Forbidden);
        html.ShouldContain("The project does not have read access");
        html.ShouldContain("role=\"alert\"");
    }

    [Fact]
    public async Task HighlightsInclusiveLinesAndLoadsTheirRemainingText()
    {
        var file = new FilePreview("/workspace/sample.txt", "sample.txt", "text", "text/plain", 100, null, []);
        var offsets = new List<int>();
        var html = await RenderAsync(file, startLine: 2, endLine: 3, readText: offset =>
        {
            offsets.Add(offset);
            return offset == 0 ? new FilePreviewText("first\r\nse", 9) : new FilePreviewText("cond\r\nthird\r\nfourth", null);
        });
        offsets.ShouldBe([0, 9]);
        WebUtility.HtmlDecode(html).ShouldContain("Lines 2–3");
        html.ShouldContain("data-start-line=\"2\"");
        html.ShouldContain("--selection-offset:1;--selection-lines:2");
        WebUtility.HtmlDecode(html).ShouldContain("second\nthird");
        html.ShouldNotContain("beyond");
    }

    [Theory]
    [InlineData(3, 5, true, true)]
    [InlineData(9, 9, false, true)]
    [InlineData(0, 2, false, false)]
    [InlineData(3, 2, false, false)]
    public async Task HandlesMissingAndInvalidLocations(int start, int end, bool highlighted, bool missing)
    {
        var file = new FilePreview("/workspace/sample.txt", "sample.txt", "text", "text/plain", 100, null, []);
        var html = await RenderAsync(file, new FilePreviewText("first\nsecond\nthird", null), startLine: start, endLine: end);
        html.Contains("class=\"file-preview-selection\"", StringComparison.Ordinal).ShouldBe(highlighted);
        html.Contains("beyond the end", StringComparison.Ordinal).ShouldBe(missing);
        if (highlighted) html.ShouldContain("--selection-lines:1");
    }

    [Fact]
    public async Task StopsLoadingAtThePreviewLimitWhenRequestedLinesAreMissing()
    {
        var file = new FilePreview("/workspace/sample.txt", "sample.txt", "text", "text/plain", 2_000_000, null, []);
        var pages = 0;
        var html = await RenderAsync(file, startLine: 100, readText: offset =>
        {
            pages++;
            return new FilePreviewText(new string('x', 32768), offset + 32768);
        });
        pages.ShouldBe(32);
        html.ShouldContain("requested location extends beyond the 1 MB");
        html.ShouldNotContain("Load more text");
    }

    private static async Task<string> RenderAsync(FilePreview file, FilePreviewText? text = null, HttpStatusCode status = HttpStatusCode.OK,
        int? startLine = null, int? endLine = null, Func<int, FilePreviewText>? readText = null)
    {
        var registrations = new ServiceCollection();
        registrations.AddSingleton(new HttpClient(new Handler(file, text, status, readText)) { BaseAddress = new Uri("https://host.test/") });
        var url = new Mock<IApiBaseUrl>();
        url.SetupGet(value => value.Value).Returns(new Uri("https://host.test/"));
        registrations.AddSingleton(url.Object);
        registrations.AddSingleton(Mock.Of<IJSRuntime>());
        await using var services = registrations.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<FilePreviewPanel>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(FilePreviewPanel.ProjectId)] = Guid.NewGuid(), [nameof(FilePreviewPanel.Path)] = file.Path,
                [nameof(FilePreviewPanel.StartLine)] = startLine, [nameof(FilePreviewPanel.EndLine)] = endLine
            }));
            return component.ToHtmlString();
        });
    }

    private sealed class Handler(FilePreview file, FilePreviewText? text, HttpStatusCode status,
        Func<int, FilePreviewText>? readText) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = request.RequestUri!.AbsolutePath.EndsWith("preview-text", StringComparison.Ordinal)
                    ? JsonContent.Create(readText?.Invoke(int.Parse(request.RequestUri.Query.Split("&offset=", StringSplitOptions.None)[1],
                        System.Globalization.CultureInfo.InvariantCulture)) ?? text) : JsonContent.Create(file)
            });
    }
}
