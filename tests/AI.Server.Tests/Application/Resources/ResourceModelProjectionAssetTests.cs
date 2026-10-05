namespace AI.Application.Tests.Resources;

using AI.Application.Resources;
using AI.Contracts.Resources;
using Moq;
using Shouldly;
using Xunit;

public sealed class ResourceModelProjectionAssetTests
{
    [Fact]
    public async Task IncludesUploadedTextButReportsBinaryContentAsUnavailable()
    {
        var projectId = Guid.NewGuid();
        var assets = new Mock<IResourceAssetService>();
        assets.Setup(service => service.ReadTextAsync(projectId, "text", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResourceAssetText("Relevant lines", false));
        assets.Setup(service => service.ReadTextAsync(projectId, "binary", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResourceAssetText?)null);
        var resources = new[]
        {
            new ChatResource(Guid.NewGuid(), ChatResourceKind.File, "notes.txt", "notes.txt",
                Source: ChatResourceSource.Upload, AssetId: "text", MediaType: "text/plain", Size: 14),
            new ChatResource(Guid.NewGuid(), ChatResourceKind.File, "data.bin", "data.bin",
                Source: ChatResourceSource.Upload, AssetId: "binary", MediaType: "application/octet-stream", Size: 20)
        };

        var result = await new ResourceModelProjection(null, assets.Object)
            .ProjectAsync(projectId, Guid.NewGuid(), "Summarize these files", resources, CancellationToken.None);

        result.ShouldContain("Relevant lines");
        result.ShouldContain("Binary content is not available as text");
        result.ShouldNotContain("live path; contents not loaded");
    }
}
