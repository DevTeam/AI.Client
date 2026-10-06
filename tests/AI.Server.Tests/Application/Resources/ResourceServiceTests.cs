namespace AI.Application.Tests.Resources;

using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Resources;
using AI.Application.Skills;
using AI.Contracts.Skills;
using AI.Contracts.Projects;
using AI.Contracts.Resources;
using AI.Infrastructure.Storage;
using AI.Infrastructure.Workspace;
using Moq;
using Shouldly;
using Xunit;

public sealed class ResourceServiceTests
{
    [Fact]
    public async Task ShouldValidateUploadedFileInsideTheSelectedProjectWithoutAWorkspaceGrant()
    {
        var projectId = Guid.NewGuid();
        var assetId = new string('a', 64);
        var assets = new Mock<IResourceAssetService>();
        assets.Setup(service => service.ReadAsync(projectId, assetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResourceAsset([65, 66], "text/plain"));
        var service = new ResourceService(new Mock<IProjectService>().Object,
            new PhysicalDirectoryBrowser(), new Mock<IResourceRepository>().Object,
            new Mock<IReviewService>().Object, new ProjectPathAccess(), new Mock<ISkillCatalog>().Object,
            new Mock<IChatService>().Object, new Mock<IWorkspaceDiffReader>().Object,
            new FileExcerptReader(), assets.Object);
        var reference = new ChatResource(Guid.NewGuid(), ChatResourceKind.File, "note.txt", "note.txt",
            Source: ChatResourceSource.Upload, AssetId: assetId);

        var validated = await service.ValidateForChatAsync(projectId, Guid.NewGuid(), [reference], CancellationToken.None);

        validated.ShouldHaveSingleItem().Kind.ShouldBe(ChatResourceKind.File);
        validated[0].MediaType.ShouldBe("text/plain");
        await Should.ThrowAsync<InvalidOperationException>(() => service.ValidateForChatAsync(
            Guid.NewGuid(), Guid.NewGuid(), [reference], CancellationToken.None));
    }

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
                new Mock<IReviewService>().Object, new ProjectPathAccess(), new Mock<ISkillCatalog>().Object, new Mock<IChatService>().Object, new Mock<IWorkspaceDiffReader>().Object, new FileExcerptReader(), new Mock<IResourceAssetService>().Object);

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
            new Mock<ISkillCatalog>().Object, new Mock<IChatService>().Object, new Mock<IWorkspaceDiffReader>().Object, new FileExcerptReader(), new Mock<IResourceAssetService>().Object);
        var token = TestContext.Current.CancellationToken;

        var validated = await service.ValidateForChatAsync(projectId, chatId,
            [new ChatResource(reviewId, ChatResourceKind.Review, string.Empty, "Old name")], token);

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
            new Mock<ISkillCatalog>().Object, new Mock<IChatService>().Object, new Mock<IWorkspaceDiffReader>().Object, new FileExcerptReader(), new Mock<IResourceAssetService>().Object);

        await Should.ThrowAsync<InvalidOperationException>(() => service.ValidateForChatAsync(projectId, chatId,
            [new ChatResource(review.Id, ChatResourceKind.Review, string.Empty)],
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
            catalog.Object, new Mock<IChatService>().Object, new Mock<IWorkspaceDiffReader>().Object, new FileExcerptReader(), new Mock<IResourceAssetService>().Object);
        var token = TestContext.Current.CancellationToken;

        var validated = await service.ValidateForChatAsync(projectId, chatId,
            [new ChatResource(Guid.NewGuid(), ChatResourceKind.Skill, "project-name", "Stale name")], token);

        validated.ShouldHaveSingleItem().Name.ShouldBe("Project name");
        await Should.ThrowAsync<InvalidOperationException>(() => service.ValidateForChatAsync(projectId, chatId,
            [new ChatResource(Guid.NewGuid(), ChatResourceKind.Skill, "off")], token));
        await Should.ThrowAsync<InvalidOperationException>(() => service.ValidateForChatAsync(projectId, chatId,
            [new ChatResource(Guid.NewGuid(), ChatResourceKind.Skill, "missing")], token));
        await Should.ThrowAsync<ArgumentException>(() => service.ValidateForChatAsync(projectId, chatId,
            [new ChatResource(Guid.NewGuid(), ChatResourceKind.Skill, "project-name"),
                new ChatResource(Guid.NewGuid(), ChatResourceKind.Skill, "project-name")], token));
    }

    [Fact]
    public void ShouldTellTheModelWhichSkillTheUserInvoked()
    {
        var projected = new ResourceModelProjection().Project("Make it shorter",
            [new ChatResource(Guid.NewGuid(), ChatResourceKind.Skill, "project-name", "Project name")]);

        projected.ShouldStartWith("The user invoked the skill \"project-name\" (\"Project name\")");
        projected.ShouldContain("mcp_app__run_skill");
        projected.ShouldEndWith("\nMake it shorter");
        projected.ShouldNotContain("Attached workspace references");
    }

    [Fact]
    public async Task ShouldCaptureTheChosenLinesWhenSentAndKeepTheLink()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-client-resources-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var source = Path.Combine(root, "source.cs");
            await File.WriteAllTextAsync(source, "one\ntwo\nthree\nfour\n", token);
            var projectId = Guid.CreateVersion7();
            var projects = ProjectsWith(projectId, root);
            var location = new Mock<IProjectStorageLocation>();
            location.SetupGet(item => item.RootDirectory).Returns(root);
            using var repository = new JsonResourceRepository(location.Object, new PhysicalTextFileSystem());
            var service = new ResourceService(projects.Object, new PhysicalDirectoryBrowser(), repository,
                new Mock<IReviewService>().Object, new ProjectPathAccess(), new Mock<ISkillCatalog>().Object,
                new Mock<IChatService>().Object, new Mock<IWorkspaceDiffReader>().Object, new FileExcerptReader(), new Mock<IResourceAssetService>().Object);
            var created = await service.CreateAsync(projectId, ChatResourceKind.File, source, token);

            var validated = await service.ValidateForChatAsync(projectId, Guid.NewGuid(),
                [created with { Lines = new ChatLineRange(2, 3), Excerpt = "forged by the client", Mention = "@source.cs:2-3" }], token);

            var file = validated.ShouldHaveSingleItem();
            file.Excerpt.ShouldBe("two\nthree\n");
            file.Mention.ShouldBe("@source.cs:2-3");
            await Should.ThrowAsync<ArgumentException>(() => service.ValidateForChatAsync(projectId, Guid.NewGuid(),
                [created with { Lines = new ChatLineRange(9, 12) }], token));
            // Without lines there is nothing to capture: an excerpt the client sent is dropped.
            (await service.ValidateForChatAsync(projectId, Guid.NewGuid(), [created with { Excerpt = "forged" }], token))
                .ShouldHaveSingleItem().Excerpt.ShouldBeNull();
        }
        finally
        {
            // The target is generated below the explicitly chosen temporary test root.
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ShouldCaptureTheWholeFileWhenTheMessageSendsItsContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-client-resources-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var source = Path.Combine(root, "source.cs");
            var empty = Path.Combine(root, "empty.cs");
            await File.WriteAllTextAsync(source, "class A { }\n", token);
            await File.WriteAllTextAsync(empty, string.Empty, token);
            var projectId = Guid.CreateVersion7();
            var projects = ProjectsWith(projectId, root);
            var location = new Mock<IProjectStorageLocation>();
            location.SetupGet(item => item.RootDirectory).Returns(root);
            using var repository = new JsonResourceRepository(location.Object, new PhysicalTextFileSystem());
            var assets = new ResourceAssetService(location.Object, projects.Object);
            var service = new ResourceService(projects.Object, new PhysicalDirectoryBrowser(), repository,
                new Mock<IReviewService>().Object, new ProjectPathAccess(), new Mock<ISkillCatalog>().Object,
                new Mock<IChatService>().Object, new Mock<IWorkspaceDiffReader>().Object, new FileExcerptReader(), assets);
            var created = await service.CreateAsync(projectId, ChatResourceKind.File, source, token);
            var createdEmpty = await service.CreateAsync(projectId, ChatResourceKind.File, empty, token);

            var validated = await service.ValidateForChatAsync(projectId, Guid.NewGuid(), [
                created with { IncludeContent = true, Mention = "@source.cs" },
                createdEmpty with { IncludeContent = true }], token);

            // The file as it was when sent, kept with its path; the request itself is not stored.
            var file = validated[0];
            file.IsContentSnapshot.ShouldBeTrue();
            file.Path.ShouldBe(created.Path);
            file.Name.ShouldBe("source.cs");
            file.Size.ShouldBe(12);
            file.Mention.ShouldBe("@source.cs");
            file.IncludeContent.ShouldBeFalse();
            (await assets.ReadTextAsync(projectId, file.AssetId!, token))!.Text.ShouldBe("class A { }\n");
            // An empty file has nothing to send but its path.
            validated[1].AssetId.ShouldBeNull();
            validated[1].IncludeContent.ShouldBeFalse();

            var projected = await new ResourceModelProjection(new Mock<IReviewService>().Object, assets)
                .ProjectAsync(projectId, Guid.NewGuid(), "Look at @source.cs", [file], token);
            projected.ShouldContain($"- file: {System.Text.Json.JsonSerializer.Serialize(created.Path)} (linked in the message as \"@source.cs\")");
            projected.ShouldContain("  ```\n  class A { }\n  ```");
        }
        finally
        {
            // The target is generated below the explicitly chosen temporary test root.
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ShouldNameLinkedChatsAndProjectsAndCaptureUncommittedChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-client-resources-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var projectId = Guid.CreateVersion7();
            var chatId = Guid.CreateVersion7();
            var otherChatId = Guid.CreateVersion7();
            var projects = ProjectsWith(projectId, root);
            var chats = new Mock<IChatService>();
            chats.Setup(item => item.ListAsync(projectId, It.IsAny<CancellationToken>())).ReturnsAsync([
                new AI.Contracts.Chats.ChatSummary(chatId, projectId, "This chat", DateTimeOffset.UnixEpoch, 1, DateTimeOffset.UnixEpoch),
                new AI.Contracts.Chats.ChatSummary(otherChatId, projectId, "Deploy fix", DateTimeOffset.UnixEpoch, 1, DateTimeOffset.UnixEpoch)]);
            var diffs = new Mock<IWorkspaceDiffReader>();
            diffs.Setup(item => item.FindRepository(It.IsAny<string>())).Returns(root);
            diffs.Setup(item => item.ReadDiff(It.IsAny<string>())).Returns("diff --git a/x b/x");
            var service = new ResourceService(projects.Object, new PhysicalDirectoryBrowser(), new Mock<IResourceRepository>().Object,
                new Mock<IReviewService>().Object, new ProjectPathAccess(), new Mock<ISkillCatalog>().Object,
                chats.Object, diffs.Object, new FileExcerptReader(), new Mock<IResourceAssetService>().Object);

            var validated = await service.ValidateForChatAsync(projectId, chatId, [
                new ChatResource(Guid.NewGuid(), ChatResourceKind.Chat, otherChatId.ToString(), "Stale", Mention: "@chat:\"Deploy fix\""),
                new ChatResource(Guid.NewGuid(), ChatResourceKind.Project, projectId.ToString()),
                new ChatResource(Guid.NewGuid(), ChatResourceKind.Diff, root, Excerpt: "forged", Mention: "@diff")], token);

            validated[0].Name.ShouldBe("Deploy fix");
            validated[0].Mention.ShouldBe("@chat:\"Deploy fix\"");
            validated[1].Name.ShouldBe("Project");
            validated[2].Excerpt.ShouldBe("diff --git a/x b/x");
            validated[2].Mention.ShouldBe("@diff");
            await Should.ThrowAsync<ArgumentException>(() => service.ValidateForChatAsync(projectId, chatId,
                [new ChatResource(Guid.NewGuid(), ChatResourceKind.Chat, chatId.ToString())], token));
            await Should.ThrowAsync<InvalidOperationException>(() => service.ValidateForChatAsync(projectId, chatId,
                [new ChatResource(Guid.NewGuid(), ChatResourceKind.Diff, Path.GetTempPath())], token));
            await Should.ThrowAsync<ArgumentException>(() => service.ValidateForChatAsync(projectId, chatId,
                [new ChatResource(Guid.NewGuid(), ChatResourceKind.Project, projectId.ToString(), Mention: "no at sign")], token));
        }
        finally
        {
            // The target is generated below the explicitly chosen temporary test root.
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ShouldOfferEveryRepositoryWithChangesFoundInAProjectDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-client-resources-" + Guid.NewGuid().ToString("N"));
        var first = Path.Combine(root, "a", "app");
        var second = Path.Combine(root, "b", "app");
        var clean = Path.Combine(root, "clean");
        foreach (var directory in new[] { first, second, clean }) Directory.CreateDirectory(directory);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var projectId = Guid.CreateVersion7();
            var diffs = new Mock<IWorkspaceDiffReader>();
            // The granted folder holds checkouts but is none itself.
            diffs.Setup(item => item.FindRepository(It.IsAny<string>())).Returns((string?)null);
            diffs.Setup(item => item.FindNestedRepositories(It.IsAny<string>())).Returns([first, second, clean]);
            diffs.Setup(item => item.ChangedFiles(first)).Returns(["x.cs"]);
            diffs.Setup(item => item.ChangedFiles(second)).Returns(["y.cs", "z.cs"]);
            diffs.Setup(item => item.ChangedFiles(clean)).Returns([]);
            var service = new ResourceService(ProjectsWith(projectId, root).Object, new PhysicalDirectoryBrowser(),
                new Mock<IResourceRepository>().Object, new Mock<IReviewService>().Object, new ProjectPathAccess(),
                new Mock<ISkillCatalog>().Object, new Mock<IChatService>().Object, diffs.Object, new FileExcerptReader(), new Mock<IResourceAssetService>().Object);

            var sources = await service.ListDiffSourcesAsync(projectId, token);

            // Both are called "app", so each is named by where it is.
            sources.Select(item => (item.Name, item.ChangedFiles)).ShouldBe([("Workspace/a/app", 1), ("Workspace/b/app", 2)]);
        }
        finally
        {
            // The target is generated below the explicitly chosen temporary test root.
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ShouldShowTheModelCapturedTextAndWhereEachLinkIs()
    {
        var chatId = Guid.NewGuid();
        var projected = new ResourceModelProjection().Project("Compare @app.cs:2-3 with @chat:x", [
            new ChatResource(Guid.NewGuid(), ChatResourceKind.File, "C:\\repo\\app.cs", Lines: new ChatLineRange(2, 3),
                Excerpt: "two\n```\nthree\n", Mention: "@app.cs:2-3"),
            new ChatResource(Guid.NewGuid(), ChatResourceKind.Chat, chatId.ToString(), "x", Mention: "@chat:x"),
            new ChatResource(Guid.NewGuid(), ChatResourceKind.Diff, "C:\\repo", "repo", Excerpt: "+added")]);

        projected.ShouldContain("lines 2-3 (linked in the message as \"@app.cs:2-3\")");
        // The captured text holds a fence of its own, so the one around it is longer.
        projected.ShouldContain("  ````\n  two\n  ```\n  three\n  ````");
        projected.ShouldContain($"- chat: \"x\" (linked in the message as \"@chat:x\") [chat {chatId}");
        projected.ShouldContain("  ```diff\n  +added\n  ```");
    }

    private static Mock<IProjectService> ProjectsWith(Guid projectId, string root)
    {
        var project = new ProjectDetails(projectId, "Project", "", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1,
            [new DirectoryGrantSettings(Guid.CreateVersion7(), "Workspace", root, true, ["read"])], [], []);
        var projects = new Mock<IProjectService>();
        projects.Setup(item => item.GetAsync(projectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);
        return projects;
    }

    private static SkillDefinition Skill(string id, string name, bool enabled) =>
        new(id, name, "Description", "User", "---", enabled, System.Text.Json.JsonDocument.Parse("{}").RootElement);
}
