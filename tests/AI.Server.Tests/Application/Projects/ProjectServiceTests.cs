namespace AI.Application.Tests.Projects;

using AI.Application.Projects;
using AI.Application.Settings;
using AI.Contracts.Projects;
using AI.Contracts.Settings;
using Domain.Common;
using AI.Domain.Projects;
using Moq;
using Shouldly;
using Xunit;

public class ProjectServiceTests
{
    private readonly Mock<IProjectRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IIdGenerator> _idGenerator = new(MockBehavior.Strict);
    private readonly Mock<IClock> _clock = new(MockBehavior.Strict);
    private readonly Mock<IGlobalSettingsRepository> _globalSettingsRepository = new(MockBehavior.Strict);
    private readonly DateTimeOffset _now = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);
    private readonly ProjectId _projectId = new(Guid.Parse("019f0000-0000-7000-8000-000000000001"));

    [Fact]
    public async Task ShouldCreateProjectWithGeneratedIdAndCurrentTime()
    {
        // Given
        var service = CreateInstance();
        _idGenerator.Setup(i => i.Create()).Returns(_projectId.Value);
        _clock.SetupGet(i => i.UtcNow).Returns(_now);
        _repository
            .Setup(i => i.SaveAsync(It.IsAny<Project>(), 0, CancellationToken.None))
            .ReturnsAsync(ProjectSaveResult.Saved(1));
        _globalSettingsRepository.Setup(i => i.LoadAsync(CancellationToken.None))
            .ReturnsAsync(new GlobalSettings([], [], []));

        // When
        var project = await service.CreateAsync(new CreateProjectRequest(" Project ", " Description "), CancellationToken.None);

        // Then
        project.Id.ShouldBe(_projectId.Value);
        project.Name.ShouldBe("Project");
        project.Description.ShouldBe("Description");
        project.CreatedAt.ShouldBe(_now);
        project.Revision.ShouldBe(1);
        project.ConnectionId.ShouldBeNull();
        _repository.Verify(i => i.SaveAsync(
            It.Is<Project>(j => j.Id == _projectId && j.ConnectionId == null),
            0,
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ShouldCreateProjectWithoutPinningToGlobalDefault()
    {
        // Given — the global default exists, but a new project must not adopt it, otherwise the
        // user has to overwrite the field the moment they want to follow the global default.
        var service = CreateInstance();
        _idGenerator.Setup(i => i.Create()).Returns(_projectId.Value);
        _clock.SetupGet(i => i.UtcNow).Returns(_now);
        _repository
            .Setup(i => i.SaveAsync(It.IsAny<Project>(), 0, CancellationToken.None))
            .ReturnsAsync(ProjectSaveResult.Saved(1));
        _globalSettingsRepository.Setup(i => i.LoadAsync(CancellationToken.None))
            .ReturnsAsync(new GlobalSettings(
                Connections: [new ConnectionSettings(Guid.Parse("019f0000-0000-7000-8000-0000000000aa"), "Default", "https://example", "m", true, true, false)],
                McpServers: [],
                ToolPolicies: []));

        // When
        var project = await service.CreateAsync(new CreateProjectRequest("P", "D"), CancellationToken.None);

        // Then
        project.ConnectionId.ShouldBeNull();
    }

    [Fact]
    public async Task ShouldListProjectsByNameIgnoringCase()
    {
        // Given
        var service = CreateInstance();
        _repository
            .Setup(i => i.ListAsync(CancellationToken.None))
            .ReturnsAsync([
                new StoredProject(CreateProject("zulu"), 1),
                new StoredProject(CreateProject("Alpha"), 2)
            ]);

        // When
        var projects = await service.ListAsync(CancellationToken.None);

        // Then
        projects.Select(i => i.Name).ShouldBe(["Alpha", "zulu"]);
        projects.Select(i => i.Revision).ShouldBe([2L, 1L]);
    }

    [Fact]
    public async Task ShouldReturnConflictWhenUpdateRevisionIsStale()
    {
        // Given
        var service = CreateInstance();
        var project = CreateProject("Project");
        _repository.Setup(i => i.GetAsync(_projectId, CancellationToken.None)).ReturnsAsync(new StoredProject(project, 1));
        _clock.SetupGet(i => i.UtcNow).Returns(_now);
        _repository
            .Setup(i => i.SaveAsync(project, 1, CancellationToken.None))
            .ReturnsAsync(ProjectSaveResult.Conflict(2));

        // When
        var result = await service.UpdateAsync(
            _projectId.Value,
            new UpdateProjectRequest("Renamed", "Description", 1),
            CancellationToken.None);

        // Then
        result.Status.ShouldBe(ProjectUpdateStatus.Conflict);
        result.Revision.ShouldBe(2);
        result.Project.ShouldBeNull();
    }

    [Fact]
    public async Task ShouldDeleteProjectUsingExpectedRevision()
    {
        // Given
        var service = CreateInstance();
        _repository
            .Setup(i => i.DeleteAsync(_projectId, 3, CancellationToken.None))
            .ReturnsAsync(ProjectDeleteResult.Deleted(3));

        // When
        var result = await service.DeleteAsync(_projectId.Value, 3, CancellationToken.None);

        // Then
        result.ShouldBe(ProjectDeleteResult.Deleted(3));
        _repository.VerifyAll();
    }

    [Fact]
    public async Task ShouldReplaceSecuritySettingsWhenRevisionMatches()
    {
        // Given
        var service = CreateInstance();
        var project = CreateProject("Project");
        var serverId = Guid.Parse("019f0000-0000-7000-8000-000000000002");
        _repository.Setup(i => i.GetAsync(_projectId, CancellationToken.None)).ReturnsAsync(new StoredProject(project, 1));
        _clock.SetupGet(i => i.UtcNow).Returns(_now);
        _repository
            .Setup(i => i.SaveAsync(project, 1, CancellationToken.None))
            .ReturnsAsync(ProjectSaveResult.Saved(2));

        // When
        var result = await service.UpdateSecurityAsync(
            _projectId.Value,
            new UpdateProjectSecurityRequest(
                1,
                [new DirectoryGrantSettings(
                    Guid.Parse("019f0000-0000-7000-8000-000000000003"),
                    "Source",
                    @"C:\Project\src",
                    true,
                    ["read", "write"])],
                [new AI.Contracts.Projects.McpServerSettings(serverId, "Files", "Stdio", true)],
                [new ToolPolicySettings(serverId, "write", "schema", "Ask", 3, 30)]),
            CancellationToken.None);

        // Then
        result.Status.ShouldBe(ProjectUpdateStatus.Updated);
        result.Project.ShouldNotBeNull();
        result.Project.Revision.ShouldBe(2);
        result.Project.DirectoryGrants.ShouldHaveSingleItem().ToolNames.ShouldBe(["read", "write"]);
        result.Project.McpServers.ShouldHaveSingleItem().Transport.ShouldBe("Stdio");
        result.Project.ToolPolicies.ShouldHaveSingleItem().Decision.ShouldBe("Ask");
    }

    [Fact]
    public async Task ShouldRejectToolPolicyWithoutConnectedServer()
    {
        // Given
        var service = CreateInstance();
        var project = CreateProject("Project");
        _repository.Setup(i => i.GetAsync(_projectId, CancellationToken.None)).ReturnsAsync(new StoredProject(project, 1));
        _clock.SetupGet(i => i.UtcNow).Returns(_now);

        // When
        // ReSharper disable once ConvertToLocalFunction
        var action = () => service.UpdateSecurityAsync(
            _projectId.Value,
            new UpdateProjectSecurityRequest(
                1,
                [],
                [],
                [new ToolPolicySettings(
                    Guid.Parse("019f0000-0000-7000-8000-000000000002"),
                    "write",
                    "schema",
                    "Ask",
                    3,
                    30)]),
            CancellationToken.None);

        // Then
        await Should.ThrowAsync<DomainException>(action);
    }

    [Fact]
    public async Task ShouldBindAnInProcessServerWhenItsToolGetsAProjectPolicy()
    {
        // Given: a project that has never heard of the Host's own application server, which is how
        // every project starts — the binding is created the first time a policy is saved for it.
        var service = CreateInstance();
        var project = CreateProject("Project");
        _repository.Setup(i => i.GetAsync(_projectId, CancellationToken.None)).ReturnsAsync(new StoredProject(project, 1));
        _repository.Setup(i => i.SaveAsync(project, 1, CancellationToken.None)).ReturnsAsync(ProjectSaveResult.Saved(2));
        _clock.SetupGet(i => i.UtcNow).Returns(_now);
        _globalSettingsRepository.Setup(i => i.LoadAsync(CancellationToken.None))
            .ReturnsAsync(new GlobalSettings([], [AppMcpServer.Settings], []));

        // When
        var result = await service.SetToolPolicyAsync(
            _projectId.Value,
            new ToolPolicySettings(AppMcpServer.Id, "app_chats", "schema", "Allow", 10, 60),
            CancellationToken.None);

        // Then
        result.ShouldNotBeNull();
        result.McpServers.ShouldHaveSingleItem().Transport.ShouldBe(AppMcpServer.Transport);
        result.ToolPolicies.ShouldHaveSingleItem().Decision.ShouldBe("Allow");
    }

    private ProjectService CreateInstance() => new(
        _repository.Object,
        _idGenerator.Object,
        _clock.Object,
        _globalSettingsRepository.Object);

    private Project CreateProject(string name) => new(_projectId, name, string.Empty, _now);
}
