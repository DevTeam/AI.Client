namespace AI.Server.Tests.Hosting;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IO.Compression;
using AI.Contracts.Projects;
using AI.Contracts.Resources;
using AI.Server.Hosting;
using Shouldly;
using Xunit;

[Trait("Category", "Integration")]
public sealed class FilePreviewEndpointTests
{
    [Fact]
    public async Task ServesByteRangesAndDownloadsAndRejectsAccessAfterRevocation()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(AppContext.BaseDirectory, "file-preview-http-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "movie.mp4");
            await File.WriteAllBytesAsync(path, Enumerable.Range(0, 100).Select(index => (byte)index).ToArray(), token);
            using var composition = new StartupComposition(new ServerOptions(Path.Combine(root, "data"), "http://127.0.0.1:0", false, StopOnProcessSignals: false));
            await using var server = await composition.Server.StartAsync(composition, token);
            using var http = new HttpClient { BaseAddress = server.Address };
            using var created = await http.PostAsJsonAsync("api/projects", new CreateProjectRequest("Preview", ""), token);
            created.EnsureSuccessStatusCode();
            var project = (await created.Content.ReadFromJsonAsync<ProjectDetails>(token))!;
            var grants = new[] { new DirectoryGrantSettings(Guid.NewGuid(), "Files", root, true, ["read"]) };
            using var granted = await http.PutAsJsonAsync($"api/projects/{project.Id}/security",
                new UpdateProjectSecurityRequest(project.Revision, grants, [], []), token);
            granted.EnsureSuccessStatusCode();
            project = (await granted.Content.ReadFromJsonAsync<ProjectDetails>(token))!;
            foreach (var (name, kind) in new[] { ("README.md", "markdown"), ("change.patch", "diff"), ("sample.zip", "archive") })
            {
                var samplePath = Path.Combine(root, name);
                if (kind == "archive")
                {
                    using var archive = ZipFile.Open(samplePath, ZipArchiveMode.Create);
                    archive.CreateEntry("src/file.txt");
                }
                else await File.WriteAllTextAsync(samplePath, "sample", token);
                var sample = await http.GetFromJsonAsync<FilePreview>(
                    $"api/projects/{project.Id}/resources/preview?path={Uri.EscapeDataString(samplePath)}", token);
                sample!.Kind.ShouldBe(kind);
                sample.ContentUrl.ShouldNotBeNull();
            }
            var invalidArchive = Path.Combine(root, "invalid.zip");
            await File.WriteAllTextAsync(invalidArchive, "invalid", token);
            using var invalid = await http.GetAsync(
                $"api/projects/{project.Id}/resources/preview?path={Uri.EscapeDataString(invalidArchive)}", token);
            invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            var preview = (await http.GetFromJsonAsync<FilePreview>($"api/projects/{project.Id}/resources/preview?path={Uri.EscapeDataString(path)}", token))!;
            using var request = new HttpRequestMessage(HttpMethod.Get, preview.ContentUrl);
            request.Headers.Range = new RangeHeaderValue(10, 19);
            using var range = await http.SendAsync(request, token);
            range.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
            (await range.Content.ReadAsByteArrayAsync(token)).ShouldBe(Enumerable.Range(10, 10).Select(index => (byte)index).ToArray());
            range.Headers.CacheControl!.NoStore.ShouldBeTrue();
            using var download = await http.GetAsync(preview.ContentUrl + "?download=true", token);
            download.EnsureSuccessStatusCode();
            download.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
            using var revoked = await http.PutAsJsonAsync($"api/projects/{project.Id}/security",
                new UpdateProjectSecurityRequest(project.Revision, [], [], []), token);
            revoked.EnsureSuccessStatusCode();
            using var denied = await http.GetAsync(preview.ContentUrl, token);
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
