namespace AI.Infrastructure.Tests.Storage;

using AI.Contracts.FileSystem;
using AI.Infrastructure.Storage;
using AI.Server.Hosting;
using Shouldly;
using Xunit;

[Trait("Category", "Integration")]
public sealed class DataDirectoryLockTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ai-client-lock-" + Guid.NewGuid().ToString("N"));

    private DataDirectoryLock NewLock() =>
        new(new ProjectStorageLocation(new ServerOptions(_root, null, true)), new SystemFileSystem());

    [Fact]
    public void ShouldRefuseASecondHolderOfTheSameDirectory()
    {
        using var first = NewLock().Acquire();

        var error = Should.Throw<DataDirectoryInUseException>(() => NewLock().Acquire());

        error.Message.ShouldContain(_root);
    }

    [Fact]
    public void ShouldLetTheDirectoryBeTakenAgainOnceReleased()
    {
        NewLock().Acquire().Dispose();

        using var second = NewLock().Acquire();

        second.ShouldNotBeNull();
    }

    [Fact]
    public void ShouldCreateAMissingDirectory()
    {
        using var held = NewLock().Acquire();

        Directory.Exists(_root).ShouldBeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
