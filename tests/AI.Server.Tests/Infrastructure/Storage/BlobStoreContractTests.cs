namespace AI.Infrastructure.Tests.Storage;

using System.Text;
using AI.Application.Resources;
using AI.Contracts.FileSystem;
using AI.Infrastructure.Storage;
using Moq;
using Shouldly;
using Xunit;

/// <summary>
/// What every <see cref="IBlobStore"/> must do (ADR-014). An implementation is tested by deriving from
/// this class and creating it; a second store — S3, say — is done when it passes the same suite.
/// </summary>
public abstract class BlobStoreContractTests
{
    protected abstract IBlobStore Create();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAbsentKeyReadsAsAbsence()
    {
        var store = Create();

        (await store.ExistsAsync("p/missing.bin", Token)).ShouldBeFalse();
        (await store.ReadAsync("p/missing.bin", Token)).ShouldBeNull();
        (await store.OpenReadAsync("p/missing.bin", Token)).ShouldBeNull();
    }

    [Fact]
    public async Task WhatIsPutIsReadBack()
    {
        var store = Create();

        await store.PutAsync("p/undo/blobs/a.bin", [1, 2, 3], Token);

        (await store.ExistsAsync("p/undo/blobs/a.bin", Token)).ShouldBeTrue();
        (await store.ReadAsync("p/undo/blobs/a.bin", Token)).ShouldBe([1, 2, 3]);
        await using var stream = await store.OpenReadAsync("p/undo/blobs/a.bin", Token);
        stream.ShouldNotBeNull();
        var buffer = new byte[3];
        await stream.ReadExactlyAsync(buffer, Token);
        buffer.ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task AKeyThatIsTakenKeepsItsFirstContent()
    {
        var store = Create();

        await store.PutAsync("p/a.bin", "first"u8.ToArray(), Token);
        await store.PutAsync("p/a.bin", "second"u8.ToArray(), Token);

        Encoding.UTF8.GetString((await store.ReadAsync("p/a.bin", Token))!).ShouldBe("first");
    }

    [Fact]
    public async Task DeletingAPrefixRemovesOnlyThatPrefix()
    {
        var store = Create();
        await store.PutAsync("p/a.bin", [1], Token);
        await store.PutAsync("p/undo/blobs/b.bin", [2], Token);
        await store.PutAsync("q/a.bin", [3], Token);

        await store.DeletePrefixAsync("p", Token);
        await store.DeletePrefixAsync("absent", Token);

        (await store.ExistsAsync("p/a.bin", Token)).ShouldBeFalse();
        (await store.ExistsAsync("p/undo/blobs/b.bin", Token)).ShouldBeFalse();
        (await store.ReadAsync("q/a.bin", Token)).ShouldBe([3]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape.bin")]
    [InlineData("p/../../escape.bin")]
    [InlineData("p//a.bin")]
    [InlineData("p\\a.bin")]
    [InlineData("C:/a.bin")]
    public async Task AKeyThatIsNotPlainSegmentsIsRefused(string key)
    {
        var store = Create();

        await Should.ThrowAsync<ArgumentException>(() => store.PutAsync(key, [1], Token));
        await Should.ThrowAsync<ArgumentException>(() => store.ReadAsync(key, Token));
    }
}

public sealed class DirectoryBlobStoreTests : BlobStoreContractTests
{
    private readonly MemoryFileSystem _files = new();

    protected override IBlobStore Create() =>
        new DirectoryBlobStore(Mock.Of<IUserStorageLocation>(location => location.RootDirectory == @"C:\data\user"),
            _files, _files.Path, new AtomicFileWriter(_files));

    [Fact]
    public async Task KeysLandUnderTheUsersAssets()
    {
        await Create().PutAsync("p/a.bin", [1], TestContext.Current.CancellationToken);

        _files.Files.Keys.ShouldContain(_files.Path.Combine(_files.Path.Combine(@"C:\data\user\assets", "p"), "a.bin"));
    }
}
