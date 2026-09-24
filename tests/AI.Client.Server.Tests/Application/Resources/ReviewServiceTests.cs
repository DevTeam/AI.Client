namespace AI.Client.Application.Tests.Resources;

using AI.Client.Application.Chats;
using AI.Client.Application.Resources;
using AI.Client.Contracts.Chats;
using AI.Client.Contracts.Resources;
using AI.Client.Contracts.Workspace;
using AI.Client.Infrastructure.Storage;
using Moq;
using Shouldly;
using Xunit;

public sealed class ReviewServiceTests
{
    [Fact]
    public async Task ShouldPersistMutableReviewAndRejectAnchorsOutsideSavedDiff()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-client-reviews-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var projectId = Guid.NewGuid();
            var chatId = Guid.NewGuid();
            var sourceId = Guid.NewGuid();
            var file = new FileChange("src/file.cs", FileChangeKind.Modified, 1, 1,
                Diff: "@@ -2,1 +2,1 @@\n-old\n+new");
            var chat = new ChatDetails(chatId, projectId, "Chat", DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch, 1, null,
                [new ChatMessageView(sourceId, null, "Assistant", "Done", DateTimeOffset.UnixEpoch,
                    WorkspaceChanges: new WorkspaceChangeSet([file], 1, 1))]);
            var chats = new Mock<IChatService>();
            chats.Setup(item => item.GetAsync(projectId, chatId, It.IsAny<CancellationToken>())).ReturnsAsync(chat);
            var otherChatId = Guid.NewGuid();
            chats.Setup(item => item.GetAsync(projectId, otherChatId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(chat with { Id = otherChatId });
            var location = new Mock<IProjectStorageLocation>();
            location.SetupGet(item => item.RootDirectory).Returns(root);
            using var repository = new JsonReviewRepository(location.Object, new PhysicalTextFileSystem());
            var service = new ReviewService(chats.Object, repository, new UnifiedDiff());

            var created = await service.CreateAsync(projectId, chatId,
                new CreateReviewRequest(sourceId, "Check behavior", [file.Path]), token);
            var comment = new ReviewComment(Guid.NewGuid(), file.Path, null, null, 2, 2, "Please fix this.");
            var updated = await service.UpdateAsync(projectId, chatId, created.Id,
                new UpdateReviewRequest("Renamed", [file.Path], [comment], created.Revision), token);

            updated!.Name.ShouldBe("Renamed");
            updated.Comments.ShouldHaveSingleItem().ShouldBe(comment);
            var restored = (await service.ListAsync(projectId, chatId, token)).ShouldHaveSingleItem();
            restored.Id.ShouldBe(updated.Id);
            restored.Name.ShouldBe("Renamed");
            restored.Files.ShouldBe([file.Path]);
            restored.Comments.ShouldHaveSingleItem().ShouldBe(comment);
            (await service.ListAsync(projectId, otherChatId, token)).ShouldBeEmpty();
            await Should.ThrowAsync<ArgumentException>(() => service.UpdateAsync(projectId, chatId, created.Id,
                new UpdateReviewRequest("Renamed", [file.Path], [comment with { NewStart = 500, NewEnd = 500 }],
                    updated.Revision), token));
            await Should.ThrowAsync<InvalidOperationException>(() => service.UpdateAsync(projectId, chatId, created.Id,
                new UpdateReviewRequest("Stale", [file.Path], [comment], created.Revision), token));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ShouldProjectCurrentReviewWithoutChangingStoredMessage()
    {
        var projectId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var reviewId = Guid.NewGuid();
        var reference = new ChatResourceRef(reviewId, ChatResourceKind.Review, string.Empty, "Old name");
        var review = new ChatReview(reviewId, projectId, chatId, "Current name", Guid.NewGuid(),
            DateTimeOffset.UnixEpoch, ["src/file.cs"],
            [new ReviewComment(Guid.NewGuid(), "src/file.cs", null, null, 3, 3, "Updated comment")],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow, 4);
        var reviews = new Mock<IReviewService>();
        reviews.Setup(item => item.ListAsync(projectId, chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([review]);

        var projected = await new ResourceModelProjection(reviews.Object).ProjectAsync(projectId, chatId,
            "Please fix", [reference], TestContext.Current.CancellationToken);

        projected.ShouldContain("Current name");
        projected.ShouldContain("Updated comment");
        projected.ShouldContain("new lines 3-3");
        reference.Name.ShouldBe("Old name");
    }
}
