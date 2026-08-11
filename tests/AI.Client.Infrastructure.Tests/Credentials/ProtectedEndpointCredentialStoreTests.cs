using AI.Client.Domain.Projects;
using AI.Client.Infrastructure.Credentials;
using AI.Client.Infrastructure.Storage;
using Shouldly;
using Xunit;

namespace AI.Client.Infrastructure.Tests.Credentials;

public class ProtectedEndpointCredentialStoreTests
{
    private readonly InMemoryTextFileSystem _fileSystem = new();
    private readonly EndpointProfileId _profileId = new(Guid.Parse("019f0000-0000-7000-8000-000000000001"));

    [Fact]
    public async Task ShouldProtectKeyBeforeWritingAndRestoreItOnRead()
    {
        // Given
        var store = CreateInstance();

        // When
        await store.SetAsync(_profileId, " secret ", CancellationToken.None);
        var key = await store.GetAsync(_profileId, CancellationToken.None);

        // Then
        key.ShouldBe("secret");
        _fileSystem.Get(GetPath()).ShouldNotContain("secret");
        (await store.ExistsAsync(_profileId, CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task ShouldDeleteCredentialWhenKeyIsEmpty()
    {
        // Given
        var store = CreateInstance();
        await store.SetAsync(_profileId, "secret", CancellationToken.None);

        // When
        await store.SetAsync(_profileId, " ", CancellationToken.None);

        // Then
        (await store.ExistsAsync(_profileId, CancellationToken.None)).ShouldBeFalse();
    }

    private ProtectedEndpointCredentialStore CreateInstance() => new(
        _fileSystem,
        new EndpointCredentialPaths("storage"),
        new PrefixDataProtector());

    private string GetPath() => new EndpointCredentialPaths("storage").GetPath(_profileId);

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

        public Task<string?> ReadTextAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(_files.GetValueOrDefault(path));

        public Task WriteTextAsync(string path, string content, CancellationToken cancellationToken)
        {
            _files[path] = content;
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
