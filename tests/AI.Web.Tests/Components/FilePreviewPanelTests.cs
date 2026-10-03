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

    private static async Task<string> RenderAsync(FilePreview file, FilePreviewText? text = null, HttpStatusCode status = HttpStatusCode.OK)
    {
        var registrations = new ServiceCollection();
        registrations.AddSingleton(new HttpClient(new Handler(file, text, status)) { BaseAddress = new Uri("https://host.test/") });
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
                [nameof(FilePreviewPanel.ProjectId)] = Guid.NewGuid(), [nameof(FilePreviewPanel.Path)] = file.Path
            }));
            return component.ToHtmlString();
        });
    }

    private sealed class Handler(FilePreview file, FilePreviewText? text, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = request.RequestUri!.AbsolutePath.EndsWith("preview-text", StringComparison.Ordinal)
                    ? JsonContent.Create(text) : JsonContent.Create(file)
            });
    }
}
