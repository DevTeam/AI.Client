// ReSharper disable UseCollectionExpression

using Moq;

namespace AI.Infrastructure.Tests.Credentials;

using AI.Domain.Projects;
using AI.Infrastructure.Credentials;
using AI.Infrastructure.Storage;
using AI.Infrastructure.Settings;
using Shouldly;
using Xunit;

public class ProtectedGlobalSecretStoreTests
{
    [Fact]
    public async Task ShouldClearAbsentSecretInAFreshDataDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ai-client-absent-" + Guid.NewGuid().ToString("N"));
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(i => i.RootDirectory).Returns(directory);
        var store = new ProtectedGlobalSecretStore(new PhysicalTextFileSystem(), new GlobalSettingsPaths(location.Object), new PrefixDataProtector());
        await store.SetAsync("mcp-env", Guid.NewGuid(), null, CancellationToken.None);
        Directory.Exists(directory).ShouldBeFalse();
    }
    private readonly InMemoryTextFileSystem _fileSystem = new();
    private readonly ConnectionId _profileId = new(Guid.Parse("019f0000-0000-7000-8000-000000000001"));

    [Fact]
    public async Task ShouldProtectKeyBeforeWritingAndRestoreItOnRead()
    {
        // Given
        var store = CreateInstance();

        // When
        await store.SetAsync("connection", _profileId.Value, " secret ", CancellationToken.None);
        var key = await store.GetAsync("connection", _profileId.Value, CancellationToken.None);

        // Then
        key.ShouldBe("secret");
        _fileSystem.Get(GetPath()).ShouldNotContain("secret");
        (await store.ExistsAsync("connection", _profileId.Value, CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task ShouldDeleteCredentialWhenKeyIsEmpty()
    {
        // Given
        var store = CreateInstance();
        await store.SetAsync("connection", _profileId.Value, "secret", CancellationToken.None);

        // When
        await store.SetAsync("connection", _profileId.Value, " ", CancellationToken.None);

        // Then
        (await store.ExistsAsync("connection", _profileId.Value, CancellationToken.None)).ShouldBeFalse();
    }

    private ProtectedGlobalSecretStore CreateInstance()
    {
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(i => i.RootDirectory).Returns("storage");
        return new ProtectedGlobalSecretStore(
            _fileSystem,
            new GlobalSettingsPaths(location.Object),
            new PrefixDataProtector());
    }

    private string GetPath()
    {
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(i => i.RootDirectory).Returns("storage");
        return new GlobalSettingsPaths(location.Object).GetSecretPath("connection", _profileId.Value);
    }

    private sealed class PrefixDataProtector : IUserDataProtector
    {
        public byte[] Protect(byte[] data) => [1, .. data];

        public byte[] Unprotect(byte[] protectedData) => protectedData[1..];
    }

    private sealed class InMemoryTextFileSystem : ITextFileSystem
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(_files.ContainsKey(path));

        public Task<IReadOnlyList<string>> ListFilesAsync(
            string directoryPath,
            string searchPattern,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

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

        public Task AppendTextAsync(string path, string content, CancellationToken cancellationToken)
        {
            _files[path] = (_files.TryGetValue(path, out var existing) ? existing : string.Empty) + content;
            return Task.CompletedTask;
        }

        public Task MoveAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(string path, CancellationToken cancellationToken)
        {
            _files.Remove(path);
            return Task.CompletedTask;
        }

        public string Get(string path) => _files[path];
    }
}
