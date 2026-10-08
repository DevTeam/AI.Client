using AI.Contracts.FileSystem;
namespace AI.Infrastructure.Tests.Storage;

using AI.Application.Projects;
using AI.Contracts.Projects;
using AI.Contracts.Resources;
using AI.Infrastructure.Storage;
using Moq;
using Shouldly;
using Xunit;

[Trait("Category", "Integration")]
public sealed class ResourceAssetServiceTests
{
    [Fact]
    public async Task ShouldKeepImageBytesInsideTheirProjectAndRemoveThemWithIt()
    {
            var root = Path.Combine(Path.GetTempPath(), "ai-client-image-test-" + Guid.NewGuid().ToString("N"));
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(item => item.RootDirectory).Returns(root);
        var projects = new Mock<IProjectService>();
        projects.Setup(item => item.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new ProjectDetails(id, "Project", string.Empty,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, [], [], []));
        var bytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEElEQVR4nGP8zwACTGCSAQANHQEDgslx/wAAAABJRU5ErkJggg==");

        try
        {
            var service = NewAssets(location.Object, projects.Object);
            var resource = await service.StoreAsync(first, bytes, "example.png", ChatResourceSource.Upload,
                "example.png", CancellationToken.None);
            var ticket = await service.CreateTicketAsync(first, resource.AssetId!, CancellationToken.None);
            resource.Kind.ShouldBe(ChatResourceKind.Image);
            var textFile = await service.StoreAsync(first, System.Text.Encoding.UTF8.GetBytes("A local note"),
                "note.txt", ChatResourceSource.Upload, "note.txt", CancellationToken.None);
            textFile.Kind.ShouldBe(ChatResourceKind.File);
            (await service.ReadTextAsync(first, textFile.AssetId!, CancellationToken.None))
                .ShouldNotBeNull().Text.ShouldBe("A local note");
            var binaryFile = await service.StoreAsync(first, [0, 1, 2], "archive.bin",
                ChatResourceSource.Upload, "archive.bin", CancellationToken.None);
            binaryFile.Kind.ShouldBe(ChatResourceKind.File);
            (await service.ReadTextAsync(first, binaryFile.AssetId!, CancellationToken.None)).ShouldBeNull();

            (await service.ReadAsync(first, resource.AssetId!, CancellationToken.None)).ShouldNotBeNull().Data.ShouldBe(bytes);
            (await service.ReadAsync(second, resource.AssetId!, CancellationToken.None)).ShouldBeNull();
            ticket.ShouldNotBeNull();
            var token = ticket.ContentUrl.Split('/')[^1];
            (await service.ReadTicketAsync(token, CancellationToken.None)).ShouldNotBeNull().Data.ShouldBe(bytes);
            var undoId = await service.StoreUndoBytesAsync(first, [], TestContext.Current.CancellationToken);
            (await service.ReadUndoBytesAsync(first, undoId, TestContext.Current.CancellationToken))!.ShouldBeEmpty();
            (await service.ReadAsync(first, undoId, TestContext.Current.CancellationToken)).ShouldBeNull();
            (await service.CreateTicketAsync(first, undoId, TestContext.Current.CancellationToken)).ShouldBeNull();
            await service.DeleteProjectAsync(first, CancellationToken.None);
            (await service.ReadAsync(first, resource.AssetId!, CancellationToken.None)).ShouldBeNull();
            (await service.ReadTicketAsync(token, CancellationToken.None)).ShouldBeNull();
            (await service.ReadUndoBytesAsync(first, undoId, TestContext.Current.CancellationToken)).ShouldBeNull();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Supplies the file-system contract the service gained; this test runs on the real disk.</summary>
    private static ResourceAssetService NewAssets(IProjectStorageLocation location, IProjectService projects)
    {
        var files = new SystemFileSystem();
        return new ResourceAssetService(location, projects, files, new SystemPath(),
            new AtomicFileWriter(files));
    }
}
