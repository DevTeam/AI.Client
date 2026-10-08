// ReSharper disable UseCollectionExpression

using Moq;

namespace AI.Infrastructure.Tests.Storage;

using AI.Application.Projects;
using AI.Domain.Projects;
using AI.Infrastructure.Storage;
using Shouldly;
using Xunit;

public class JsonProjectRepositoryTests
{
    private readonly MemoryFileSystem _fileSystem = new();
    private readonly ProjectDocumentSerializer _serializer = new();
    private readonly DateTimeOffset _createdAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);
    private readonly ProjectId _projectId = new(Guid.Parse("019f0000-0000-7000-8000-000000000001"));

    [Fact]
    public async Task ShouldSaveAndRestoreProjectWithSecuritySettings()
    {
        // Given
        var repository = CreateInstance();
        var project = CreateProjectWithSecuritySettings();

        // When
        var saveResult = await repository.SaveAsync(project, 0, CancellationToken.None);
        var restoredProject = await repository.GetAsync(_projectId, CancellationToken.None);

        // Then
        saveResult.ShouldBe(ProjectSaveResult.Saved(1));
        restoredProject.ShouldNotBeNull();
        restoredProject.Project.Name.ShouldBe("Project");
        restoredProject.Project.DirectoryGrants.ShouldHaveSingleItem().CanonicalRoot.ShouldBe(@"C:\Project\src");
        restoredProject.Project.McpServers.ShouldHaveSingleItem().DisplayName.ShouldBe("Files");
        restoredProject.Project.ToolPolicies.ShouldHaveSingleItem().Decision.ShouldBe(ToolPolicyDecision.Ask);
        restoredProject.Project.ConnectionId.ShouldBe(
            new ConnectionId(Guid.Parse("019f0000-0000-7000-8000-000000000004")));
        // The save completes by moving the temporary document onto the project one, so what is left
        // behind is the project document alone: the write's own scratch file is gone.
        _fileSystem.Files.ContainsKey(GetProjectPath()).ShouldBeTrue();
        _fileSystem.Files.ContainsKey(GetTemporaryPath()).ShouldBeFalse();
    }

    [Fact]
    public async Task ShouldReturnCurrentRevisionWhenSaveConflicts()
    {
        // Given
        var repository = CreateInstance();
        var project = CreateProject();
        await repository.SaveAsync(project, 0, CancellationToken.None);

        // When
        var result = await repository.SaveAsync(project, 0, CancellationToken.None);

        // Then
        result.ShouldBe(ProjectSaveResult.Conflict(1));
        // A conflicting save writes nothing at all: the stored document keeps the revision it had and
        // no temporary sibling is left for a later read to trip over.
        _serializer.Deserialize(_fileSystem.Files[GetProjectPath()]).Revision.ShouldBe(1);
        _fileSystem.Files.ContainsKey(GetTemporaryPath()).ShouldBeFalse();
    }

    [Fact]
    public async Task ShouldRecoverTemporaryProjectWhenCommittedFileDoesNotExist()
    {
        // Given
        var repository = CreateInstance();
        var project = CreateProject();
        _fileSystem.Files[GetTemporaryPath()] = _serializer.Serialize(project, 1);

        // When
        var restoredProject = await repository.GetAsync(_projectId, CancellationToken.None);

        // Then
        restoredProject.ShouldNotBeNull();
        restoredProject.Project.Id.ShouldBe(_projectId);
        _fileSystem.Files.ContainsKey(GetTemporaryPath()).ShouldBeFalse();
        _fileSystem.Files.ContainsKey(GetProjectPath()).ShouldBeTrue();
        // Recovered rather than thrown away: the project document now carries what the write that
        // never finished had left in its scratch file.
        _serializer.Deserialize(_fileSystem.Files[GetProjectPath()]).Revision.ShouldBe(1);
    }

    [Fact]
    public async Task ShouldDeleteTemporaryProjectWhenCommittedFileExists()
    {
        // Given
        var repository = CreateInstance();
        var project = CreateProject();
        _fileSystem.Files[GetProjectPath()] = _serializer.Serialize(project, 1);
        _fileSystem.Files[GetTemporaryPath()] = _serializer.Serialize(project, 2);

        // When
        var restoredProject = await repository.GetAsync(_projectId, CancellationToken.None);

        // Then
        restoredProject.ShouldNotBeNull();
        _fileSystem.Files.ContainsKey(GetTemporaryPath()).ShouldBeFalse();
        // The committed document wins, and the abandoned write is the one that goes away.
        _serializer.Deserialize(_fileSystem.Files[GetProjectPath()]).Revision.ShouldBe(1);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"SchemaVersion\":2}")]
    public void ShouldRejectInvalidProjectDocument(string json)
    {
        // Given

        // When
        // ReSharper disable once ConvertToLocalFunction
        var action = () => _serializer.Deserialize(json);

        // Then
        Should.Throw<Exception>(action);
    }

    private JsonProjectRepository CreateInstance()
    {
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(i => i.RootDirectory).Returns("storage");
        return new JsonProjectRepository(_fileSystem, new ProjectStoragePaths(location.Object), _serializer);
    }

    private Project CreateProject() => new(_projectId, "Project", "Description", _createdAt);

    private Project CreateProjectWithSecuritySettings()
    {
        var project = CreateProject();
        var serverId = new McpServerId(Guid.Parse("019f0000-0000-7000-8000-000000000002"));
        project.AddDirectoryGrant(
            new DirectoryGrant(
                new DirectoryGrantId(Guid.Parse("019f0000-0000-7000-8000-000000000003")),
                "Source",
                @"C:\Project\src",
                true,
                ["read", "write"]),
            _createdAt);
        project.AddMcpServer(new McpServerBinding(serverId, "Files", McpTransportKind.Stdio, true), _createdAt);
        project.SetToolPolicy(
            new ToolPolicy(new ToolIdentity(serverId, "write", "schema"), ToolPolicyDecision.Ask),
            _createdAt);
        project.SetConnection(new ConnectionId(Guid.Parse("019f0000-0000-7000-8000-000000000004")), _createdAt);
        return project;
    }

    private string GetProjectPath()
    {
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(i => i.RootDirectory).Returns("storage");
        return new ProjectStoragePaths(location.Object).GetProjectPath(_projectId);
    }

    private string GetTemporaryPath()
    {
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(i => i.RootDirectory).Returns("storage");
        return new ProjectStoragePaths(location.Object).GetTemporaryProjectPath(_projectId);
    }
}
