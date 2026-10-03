namespace AI.Application.Tests.Resources;

using AI.Application.Projects;
using AI.Application.Resources;
using AI.Contracts.Projects;
using AI.Infrastructure.Storage;
using Moq;
using Shouldly;
using Xunit;

public sealed class FilePreviewServiceTests
{
    [Theory]
    [InlineData("image.png", "image")]
    [InlineData("movie.mp4", "video")]
    [InlineData("sound.mp3", "audio")]
    [InlineData("document.pdf", "pdf")]
    [InlineData("code.cs", "text")]
    public async Task RecognizesPreviewTypesAndDoesNotExposeFilePathsInContentUrls(string name, string kind)
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, name);
        await File.WriteAllTextAsync(path, "sample", TestContext.Current.CancellationToken);
        var preview = await fixture.Service.DescribeAsync(fixture.Project.Id, path, TestContext.Current.CancellationToken);
        preview.Kind.ShouldBe(kind);
        preview.ContentUrl.ShouldNotBeNull().ShouldNotContain(name);
        var ticket = preview.ContentUrl.Split('/')[1];
        (await fixture.Service.ResolveContentAsync(ticket, TestContext.Current.CancellationToken)).ShouldBe(path);
        (await fixture.Service.ResolveContentAsync("invalid", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task DeniesUnGrantedFilesAndPreviouslyIssuedTicketsAfterAccessIsRevoked()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "secret.txt");
        await File.WriteAllTextAsync(path, "secret", TestContext.Current.CancellationToken);
        var preview = await fixture.Service.DescribeAsync(fixture.Project.Id, path, TestContext.Current.CancellationToken);
        fixture.Project = fixture.Project with { DirectoryGrants = [] };
        await Should.ThrowAsync<UnauthorizedAccessException>(() => fixture.Service.DescribeAsync(fixture.Project.Id, path, TestContext.Current.CancellationToken));
        await Should.ThrowAsync<UnauthorizedAccessException>(() => fixture.Service.ReadTextAsync(fixture.Project.Id, path, 0, TestContext.Current.CancellationToken));
        await Should.ThrowAsync<UnauthorizedAccessException>(() => fixture.Service.ResolveContentAsync(preview.ContentUrl!.Split('/')[1], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PagesUnicodeTextWithoutLosingContentAndRejectsBinaryText()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "large.txt");
        var expected = string.Concat(Enumerable.Repeat("Привет 🌴\n", 9000));
        await File.WriteAllTextAsync(path, expected, TestContext.Current.CancellationToken);
        var actual = new System.Text.StringBuilder();
        int? offset = 0;
        while (offset is { } next)
        {
            var chunk = await fixture.Service.ReadTextAsync(fixture.Project.Id, path, next, TestContext.Current.CancellationToken);
            chunk.Text.Length.ShouldBeLessThanOrEqualTo(32769);
            var roundTrip = System.Text.Json.JsonSerializer.Deserialize<AI.Contracts.Resources.FilePreviewText>(
                System.Text.Json.JsonSerializer.Serialize(chunk));
            actual.Append(roundTrip!.Text);
            offset = chunk.NextOffset;
        }
        actual.ToString().ShouldBe(expected);
        await File.WriteAllBytesAsync(path, [0, 1, 2], TestContext.Current.CancellationToken);
        (await fixture.Service.DescribeAsync(fixture.Project.Id, path, TestContext.Current.CancellationToken)).Kind.ShouldBe("binary");
        await Should.ThrowAsync<ArgumentException>(() => fixture.Service.ReadTextAsync(fixture.Project.Id, path, 0, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListsDirectoriesAndReportsMissingFiles()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "child"));
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "file.txt"), "text", TestContext.Current.CancellationToken);
        var listing = await fixture.Service.DescribeAsync(fixture.Project.Id, fixture.Root, TestContext.Current.CancellationToken);
        listing.Kind.ShouldBe("directory");
        listing.Entries.Count.ShouldBe(2);
        listing.Entries[0].IsDirectory.ShouldBeTrue();
        await Should.ThrowAsync<FileNotFoundException>(() => fixture.Service.DescribeAsync(fixture.Project.Id,
            Path.Combine(fixture.Root, "missing.txt"), TestContext.Current.CancellationToken));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ai-file-preview-" + Guid.NewGuid().ToString("N"));
        public ProjectDetails Project { get; set; }
        public FilePreviewService Service { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Project = new ProjectDetails(Guid.NewGuid(), "Test", "", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1,
                [new DirectoryGrantSettings(Guid.NewGuid(), "Files", Root, true, ["read"])], [], []);
            var projects = new Mock<IProjectService>();
            projects.Setup(service => service.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Project);
            var access = new ProjectPathAccess();
            Service = new FilePreviewService(projects.Object, new WorkspacePathResolver(projects.Object, new PhysicalDirectoryBrowser(), access), access);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
