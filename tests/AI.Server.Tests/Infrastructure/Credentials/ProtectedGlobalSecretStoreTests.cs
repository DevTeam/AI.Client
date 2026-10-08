// ReSharper disable UseCollectionExpression

using Moq;

namespace AI.Infrastructure.Tests.Credentials;

using AI.Domain.Projects;
using AI.Infrastructure.Credentials;
using AI.Infrastructure.Storage;
using AI.Infrastructure.Settings;
using AI.Infrastructure.Tests.Storage;
using Shouldly;
using Xunit;

public class ProtectedGlobalSecretStoreTests
{
    [Fact]
    public async Task ShouldClearAbsentSecretInAFreshDataDirectory()
    {
        var directory = Path.Combine("data", "absent");
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(i => i.RootDirectory).Returns(directory);
        var files = new MemoryFileSystem();
        var store = new ProtectedGlobalSecretStore(files, new GlobalSettingsPaths(location.Object), new PrefixDataProtector());
        await store.SetAsync("mcp-env", Guid.NewGuid(), null, CancellationToken.None);
        files.Files.ShouldBeEmpty();
    }
    private readonly MemoryFileSystem _fileSystem = new();
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
        _fileSystem.Files[GetPath()].ShouldNotContain("secret");
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
}
