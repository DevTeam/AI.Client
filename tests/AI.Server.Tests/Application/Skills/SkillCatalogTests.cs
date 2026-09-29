namespace AI.Application.Tests.Skills;

using AI.Application.Projects;
using AI.Application.Skills;
using AI.Contracts.Projects;
using AI.Contracts.Skills;
using AI.Infrastructure.Storage;
using AI.Infrastructure.Tests.Storage;
using AI.Server.Hosting;
using Moq;
using Shouldly;
using Xunit;

public sealed class SkillCatalogTests
{
    private const string Document = """
        ---
        id: explain-project
        name: Explain project
        description: Summarize the current project.
        parameters: {"type":"object","properties":{},"additionalProperties":false}
        result: {"type":"object","properties":{"summary":{"type":"string"}},"required":["summary"]}
        tools: []
        ---

        Return a summary as JSON.
        """;

    [Fact]
    public async Task ShouldPersistUserAndProjectSkillsWithRevisionChecks()
    {
        var projectId = Guid.CreateVersion7();
        var otherProjectId = Guid.CreateVersion7();
        var projects = new Mock<IProjectService>();
        projects.Setup(service => service.GetAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Project(projectId));
        using var catalog = new SkillCatalog(new BuiltInSkillCatalog(),
            new ProjectStorageLocation(new ServerOptions("skills-test", null, true)),
            new MemoryFileSystem(), projects.Object);

        var user = await catalog.SaveAsync(new SkillWriteRequest("User", null, Document), CancellationToken.None);
        user.Status.ShouldBe("Saved");
        user.Skill!.Revision.ShouldBe(1);
        var project = await catalog.SaveAsync(new SkillWriteRequest("Project", projectId,
            Document.Replace("Explain project", "Explain this project", StringComparison.Ordinal)), CancellationToken.None);
        project.Status.ShouldBe("Saved");
        project.Skill!.Revision.ShouldBe(1);
        (await catalog.GetByIdAsync("explain-project", projectId, CancellationToken.None))!.Source.ShouldBe("Project");
        (await catalog.GetByIdAsync("explain-project", otherProjectId, CancellationToken.None))!.Source.ShouldBe("User");

        var conflict = await catalog.SaveAsync(new SkillWriteRequest("User", null, Document), CancellationToken.None);
        conflict.Status.ShouldBe("Conflict");
        var updated = await catalog.SaveAsync(new SkillWriteRequest("User", null, Document, 1, false), CancellationToken.None);
        updated.Status.ShouldBe("Saved");
        updated.Skill!.Revision.ShouldBe(2);
        updated.Skill.Enabled.ShouldBeFalse();
        var deleted = await catalog.DeleteAsync("explain-project", "Project", projectId, 1, CancellationToken.None);
        deleted.Status.ShouldBe("Deleted");
        (await catalog.GetByIdAsync("explain-project", projectId, CancellationToken.None))!.Source.ShouldBe("User");
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("Project Name")]
    [InlineData("chat-title")]
    public async Task ShouldRejectUnsafeOrBundledIds(string id)
    {
        using var catalog = new SkillCatalog(new BuiltInSkillCatalog(),
            new ProjectStorageLocation(new ServerOptions("skills-test", null, true)),
            new MemoryFileSystem(), Mock.Of<IProjectService>());
        var content = Document.Replace("explain-project", id, StringComparison.Ordinal);

        var result = await catalog.SaveAsync(new SkillWriteRequest("User", null, content), CancellationToken.None);

        result.Status.ShouldBe("Rejected");
    }

    private static ProjectDetails Project(Guid id) => new(id, "Test", "", DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow, 1, [], [], []);
}
