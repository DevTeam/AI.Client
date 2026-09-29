namespace AI.Application.Tests.Resources;

using AI.Application.Projects;
using AI.Application.Resources;
using AI.Application.Skills;
using AI.Contracts.Skills;
using AI.Contracts.Projects;
using AI.Contracts.Resources;
using AI.Infrastructure.Storage;
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
                new Mock<IReviewService>().Object, new ProjectPathAccess(), new Mock<ISkillCatalog>().Object);

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
            new PhysicalDirectoryBrowser(), new Mock<IResourceRepository>().Object, reviews.Object, new ProjectPathAccess(),
            new Mock<ISkillCatalog>().Object);
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
            new PhysicalDirectoryBrowser(), new Mock<IResourceRepository>().Object, reviews.Object, new ProjectPathAccess(),
            new Mock<ISkillCatalog>().Object);

        await Should.ThrowAsync<InvalidOperationException>(() => service.ValidateForChatAsync(projectId, chatId,
            [new ChatResourceRef(review.Id, ChatResourceKind.Review, string.Empty)],
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldNameAnInvokedSkillAndRejectAnUnavailableOne()
    {
        var projectId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var catalog = new Mock<ISkillCatalog>();
        catalog.Setup(item => item.GetByIdAsync("project-name", projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Skill("project-name", "Project name", true));
        catalog.Setup(item => item.GetByIdAsync("off", projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Skill("off", "Off", false));
        var service = new ResourceService(new Mock<IProjectService>().Object, new PhysicalDirectoryBrowser(),
            new Mock<IResourceRepository>().Object, new Mock<IReviewService>().Object, new ProjectPathAccess(),
            catalog.Object);
        var token = TestContext.Current.CancellationToken;

        var validated = await service.ValidateForChatAsync(projectId, chatId,
            [new ChatResourceRef(Guid.NewGuid(), ChatResourceKind.Skill, "project-name", "Stale name")], token);

        validated.ShouldHaveSingleItem().Name.ShouldBe("Project name");
        await Should.ThrowAsync<InvalidOperationException>(() => service.ValidateForChatAsync(projectId, chatId,
            [new ChatResourceRef(Guid.NewGuid(), ChatResourceKind.Skill, "off")], token));
        await Should.ThrowAsync<InvalidOperationException>(() => service.ValidateForChatAsync(projectId, chatId,
            [new ChatResourceRef(Guid.NewGuid(), ChatResourceKind.Skill, "missing")], token));
        await Should.ThrowAsync<ArgumentException>(() => service.ValidateForChatAsync(projectId, chatId,
            [new ChatResourceRef(Guid.NewGuid(), ChatResourceKind.Skill, "project-name"),
                new ChatResourceRef(Guid.NewGuid(), ChatResourceKind.Skill, "project-name")], token));
    }

    [Fact]
    public void ShouldTellTheModelWhichSkillTheUserInvoked()
    {
        var projected = new ResourceModelProjection().Project("Make it shorter",
            [new ChatResourceRef(Guid.NewGuid(), ChatResourceKind.Skill, "project-name", "Project name")]);

        projected.ShouldStartWith("The user invoked the skill \"project-name\" (\"Project name\")");
        projected.ShouldContain("app_run_skill");
        projected.ShouldEndWith("\nMake it shorter");
        projected.ShouldNotContain("Attached workspace references");
    }

    private static SkillDefinition Skill(string id, string name, bool enabled) =>
        new(id, name, "Description", "User", "---", enabled, System.Text.Json.JsonDocument.Parse("{}").RootElement);
}
