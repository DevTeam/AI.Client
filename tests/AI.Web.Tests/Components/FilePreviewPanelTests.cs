namespace AI.Web.Tests.Components;

using System.Net;
using System.Net.Http.Json;
using AI.Contracts.Resources;
using AI.Web.Components;
using AI.Web.Resources;
using AI.Contracts.Workspace;
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
    [InlineData("archive", "This archive has no entries")]
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
    public async Task RendersMarkdownWithoutExecutableHtmlOrUnsafeLinks()
    {
        var file = new FilePreview("/workspace/README.md", "README.md", "markdown", "text/markdown", 100,
            "file-preview-content/ticket", []);
        var html = await RenderAsync(file, new FilePreviewText("# Read me\n\n**Bold**\n\n<script>alert(1)</script>\n\n[unsafe](javascript:alert%281%29)\n\n<javascript:alert(1)>\n\n![unsafe](data:image/svg+xml;base64,AAAA)\n\n[web](https://example.com)\n\ntext{onclick=alert(1)}", null));

        html.ShouldContain("<h1>Read me</h1>");
        html.ShouldContain("<strong>Bold</strong>");
        html.ShouldContain("Source");
        html.ShouldNotContain("<script>");
        html.ShouldNotContain("href=\"javascript:");
        html.ShouldNotContain("src=\"data:");
        html.ShouldNotContain(" onclick=");
        html.ShouldContain("href=\"https://example.com\"");
    }

    [Fact]
    public async Task RendersPatchHeadersHunksAndAddedAndRemovedLines()
    {
        var file = new FilePreview("/workspace/change.patch", "change.patch", "diff", "text/plain", 100, null, []);
        var html = await RenderAsync(file, new FilePreviewText("--- a/test\n+++ b/test\n@@ -10 +20 @@\n-before\n+<after>", null));

        html.ShouldContain("file-preview-diff");
        html.ShouldContain("diff-hunk");
        html.ShouldContain("diff-line-added");
        html.ShouldContain("diff-line-removed");
        html.ShouldContain("&lt;after&gt;");
        html.ShouldContain(">10</span>");
        html.ShouldContain(">20</span>");
    }

    [Fact]
    public async Task ShowsImplicitArchiveDirectoriesWithoutOpeningHostPaths()
    {
        var file = new FilePreview("/workspace/files.zip", "files.zip", "archive", "application/zip", 100,
            "file-preview-content/ticket", [new("src/nested/file.txt", "file.txt", false, 12, 8), new("root.txt", "root.txt", false, 10, 6)]);
        var html = await RenderAsync(file);
        html.ShouldContain("Archive root");
        html.ShouldContain(">src</span>");
        html.ShouldContain("root.txt");
        html.ShouldContain("10 bytes");
        html.ShouldNotContain("nested/file.txt");
    }

    [Fact]
    public async Task RegistersViewersThroughTheApplicationComposition()
    {
        var composition = new Composition("https://host.test/", publicWeb: true);
        var registrations = new ServiceCollection();
        registrations.AddSingleton(Mock.Of<IJSRuntime>());
        await using var services = (ServiceProvider)composition.CreateServiceProvider(composition.CreateBuilder(registrations));
        var viewers = services.GetRequiredService<IFilePreviewViewers>();
        viewers.Resolve("markdown").ShouldBe(typeof(AI.Web.Components.FilePreviews.TextFilePreview));
        viewers.Resolve("diff").ShouldBe(typeof(AI.Web.Components.FilePreviews.TextFilePreview));
        viewers.Resolve("archive").ShouldBe(typeof(AI.Web.Components.FilePreviews.ArchiveFilePreview));
        viewers.Resolve("directory").ShouldBe(typeof(AI.Web.Components.FilePreviews.DirectoryFilePreview));
        viewers.Resolve("future-format").ShouldBe(typeof(AI.Web.Components.FilePreviews.BinaryFilePreview));
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
        registrations.AddTransient<IFilePreviewApi, FilePreviewApi>();
        registrations.AddSingleton<IFilePreviewViewers>(new FilePreviewViewers([new MediaFilePreviewRegistration(),
            new TextFilePreviewRegistration(), new DirectoryFilePreviewRegistration(), new ArchiveFilePreviewRegistration()]));
        registrations.AddTransient<IFileMarkdownRenderer, FileMarkdownRenderer>();
        registrations.AddTransient<IUnifiedDiffParser, UnifiedDiff>();
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
