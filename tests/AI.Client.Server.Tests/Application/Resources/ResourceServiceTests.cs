namespace AI.Client.Application.Tests.Resources;

using AI.Client.Application.Projects;
using AI.Client.Application.Resources;
using AI.Client.Contracts.Projects;
using AI.Client.Contracts.Resources;
using AI.Client.Infrastructure.Storage;
using Moq;
using Shouldly;
using Xunit;

public sealed class ResourceServiceTests
{
    [Fact]
    public async Task ShouldReuseAProjectReferenceAndRejectItAfterRetirement()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-client-resources-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var source = Path.Combine(root, "source.cs");
            await File.WriteAllTextAsync(source, "private content that must not enter a chat", token);
            var projectId = Guid.CreateVersion7();
            var project = new ProjectDetails(projectId, "Project", "", DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch, 1,
                [new DirectoryGrantSettings(Guid.CreateVersion7(), "Workspace", root, true, ["read"])], [], []);
            var projects = new Mock<IProjectService>();
            projects.Setup(item => item.GetAsync(projectId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(project);
            var location = new Mock<IProjectStorageLocation>();
            location.SetupGet(item => item.RootDirectory).Returns(root);
            using var repository = new JsonResourceRepository(location.Object, new PhysicalTextFileSystem());
            var service = new ResourceService(projects.Object, new PhysicalDirectoryBrowser(), repository,
                new Mock<IReviewService>().Object);

            var first = await service.CreateAsync(projectId, ChatResourceKind.File, source, token);
            var second = await service.CreateAsync(projectId, ChatResourceKind.File, source, token);
            first.ShouldBe(second);
            (await service.ValidateAsync(projectId, [first], token)).ShouldHaveSingleItem();
            (await service.ListAsync(projectId, token)).Count.ShouldBe(1);
            var stored = await File.ReadAllTextAsync(Path.Combine(root, "resources", $"{projectId}.json"), token);
            stored.ShouldNotContain("private content that must not enter a chat");

            var retired = await service.RetireAsync(projectId, first.Id, 1, token);
            retired!.Retired.ShouldBeTrue();
            await Should.ThrowAsync<InvalidOperationException>(() =>
                service.ValidateAsync(projectId, [first], token));
        }
        finally
        {
            // The target is generated below the explicitly chosen temporary test root.
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ShouldAcceptOnlyReviewsInTheTargetChat()
    {
        var projectId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var reviewId = Guid.NewGuid();
        var review = new ChatReview(reviewId, projectId, chatId, "Current name", Guid.NewGuid(),
            DateTimeOffset.UnixEpoch, ["file.cs"],
            [new ReviewComment(Guid.NewGuid(), "file.cs", null, null, 1, 1, "Check this line")],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1);
        var reviews = new Mock<IReviewService>();
        reviews.Setup(item => item.ListAsync(projectId, chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([review]);
        var otherChatId = Guid.NewGuid();
        reviews.Setup(item => item.ListAsync(projectId, otherChatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = new ResourceService(new Mock<IProjectService>().Object,
            new PhysicalDirectoryBrowser(), new Mock<IResourceRepository>().Object, reviews.Object);
        var token = TestContext.Current.CancellationToken;

        var validated = await service.ValidateForChatAsync(projectId, chatId,
            [new ChatResourceRef(reviewId, ChatResourceKind.Review, string.Empty, "Old name")], token);

        validated.ShouldHaveSingleItem().Name.ShouldBe("Current name");
        await Should.ThrowAsync<InvalidOperationException>(() => service.ValidateForChatAsync(projectId,
            otherChatId, [validated[0]], token));
    }

    [Theory]
    [InlineData(ChatReviewKind.Diff)]
    [InlineData(ChatReviewKind.Message)]
    public async Task ShouldRejectReviewWithoutComments(ChatReviewKind kind)
    {
        var projectId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var review = new ChatReview(Guid.NewGuid(), projectId, chatId, "Empty", Guid.NewGuid(),
            DateTimeOffset.UnixEpoch, kind == ChatReviewKind.Diff ? ["file.cs"] : [], [],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, kind,
            kind == ChatReviewKind.Message ? [] : null);
        var reviews = new Mock<IReviewService>();
        reviews.Setup(item => item.ListAsync(projectId, chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([review]);
        var service = new ResourceService(new Mock<IProjectService>().Object,
            new PhysicalDirectoryBrowser(), new Mock<IResourceRepository>().Object, reviews.Object);

        await Should.ThrowAsync<InvalidOperationException>(() => service.ValidateForChatAsync(projectId, chatId,
            [new ChatResourceRef(review.Id, ChatResourceKind.Review, string.Empty)],
            TestContext.Current.CancellationToken));
    }
}
