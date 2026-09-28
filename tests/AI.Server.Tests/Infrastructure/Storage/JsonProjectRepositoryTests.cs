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
    private readonly InMemoryTextFileSystem _fileSystem = new();
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
        _fileSystem.MoveOperations.ShouldHaveSingleItem().ShouldBe((GetTemporaryPath(), GetProjectPath(), true));
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
        _fileSystem.MoveOperations.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ShouldRecoverTemporaryProjectWhenCommittedFileDoesNotExist()
    {
        // Given
        var repository = CreateInstance();
        var project = CreateProject();
        _fileSystem.Add(GetTemporaryPath(), _serializer.Serialize(project, 1));

        // When
        var restoredProject = await repository.GetAsync(_projectId, CancellationToken.None);

        // Then
        restoredProject.ShouldNotBeNull();
        restoredProject.Project.Id.ShouldBe(_projectId);
        _fileSystem.Exists(GetTemporaryPath()).ShouldBeFalse();
        _fileSystem.Exists(GetProjectPath()).ShouldBeTrue();
        _fileSystem.MoveOperations.ShouldHaveSingleItem().ShouldBe((GetTemporaryPath(), GetProjectPath(), false));
    }

    [Fact]
    public async Task ShouldDeleteTemporaryProjectWhenCommittedFileExists()
    {
        // Given
        var repository = CreateInstance();
        var project = CreateProject();
        _fileSystem.Add(GetProjectPath(), _serializer.Serialize(project, 1));
        _fileSystem.Add(GetTemporaryPath(), _serializer.Serialize(project, 2));

        // When
        var restoredProject = await repository.GetAsync(_projectId, CancellationToken.None);

        // Then
        restoredProject.ShouldNotBeNull();
        _fileSystem.Exists(GetTemporaryPath()).ShouldBeFalse();
        _fileSystem.DeleteOperations.ShouldHaveSingleItem().ShouldBe(GetTemporaryPath());
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

    private sealed class InMemoryTextFileSystem : ITextFileSystem
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

        public List<(string Source, string Destination, bool Overwrite)> MoveOperations { get; } = [];

        public List<string> DeleteOperations { get; } = [];

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken) => Task.FromResult(Exists(path));

        public Task<IReadOnlyList<string>> ListFilesAsync(
            string directoryPath,
            string searchPattern,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(_files.Keys
                .Where(path => path.StartsWith(directoryPath, StringComparison.Ordinal)
                               && path.EndsWith(".json", StringComparison.Ordinal))
                .ToArray());

        public Task<IReadOnlyList<string>> ListFilesRecursivelyAsync(string directoryPath, string searchPattern, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(_files.Keys.Where(path => path.StartsWith(directoryPath, StringComparison.Ordinal)
                && System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(searchPattern, Path.GetFileName(path))).ToArray());

        public Task<string?> ReadTextAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(_files.GetValueOrDefault(path));

        public Task WriteTextAsync(string path, string content, CancellationToken cancellationToken)
        {
            _files[path] = content;
            return Task.CompletedTask;
        }

        public Task MoveAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken)
        {
            if (!_files.TryGetValue(sourcePath, out var content))
            {
                throw new FileNotFoundException(sourcePath);
            }

            if (!overwrite && _files.ContainsKey(destinationPath))
            {
                throw new IOException(destinationPath);
            }

            _files[destinationPath] = content;
            _files.Remove(sourcePath);
            MoveOperations.Add((sourcePath, destinationPath, overwrite));
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string path, CancellationToken cancellationToken)
        {
            _files.Remove(path);
            DeleteOperations.Add(path);
            return Task.CompletedTask;
        }

        public void Add(string path, string content) => _files.Add(path, content);

        public bool Exists(string path) => _files.ContainsKey(path);
    }
}
