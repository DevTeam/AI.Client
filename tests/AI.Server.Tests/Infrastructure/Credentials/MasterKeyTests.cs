namespace AI.Infrastructure.Tests.Credentials;

using System.Security.Cryptography;
using System.Text;
using AI.Infrastructure.Credentials;
using AI.Infrastructure.Storage;
using AI.Server.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

public sealed class MasterKeyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ai-client-keys-" + Guid.NewGuid().ToString("N"));
    private readonly MasterKeyFormat _format = new();

    private FileMasterKeyStore NewFileStore() =>
        new(new ProjectStorageLocation(new ServerOptions(_root, null, true)), _format);

    private static ILoggerProvider Logs()
    {
        var provider = new Mock<ILoggerProvider>();
        provider.Setup(item => item.CreateLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
        return provider.Object;
    }

    [Fact]
    public void ProtectorShouldRoundTripAndUseAFreshNonceEachTime()
    {
        var protector = new MasterKeyUserDataProtector(new FixedStore(_format.Create()));
        var secret = Encoding.UTF8.GetBytes("sk-secret");

        var first = protector.Protect(secret);
        var second = protector.Protect(secret);

        first.ShouldNotBe(second);
        protector.Unprotect(first).ShouldBe(secret);
        protector.Unprotect(second).ShouldBe(secret);
    }

    [Fact]
    public void ProtectorShouldRejectATamperedValue()
    {
        var protector = new MasterKeyUserDataProtector(new FixedStore(_format.Create()));
        var value = protector.Protect(Encoding.UTF8.GetBytes("sk-secret"));
        value[^1] ^= 1;

        Should.Throw<AuthenticationTagMismatchException>(() => protector.Unprotect(value));
    }

    [Fact]
    public void ProtectorShouldRejectAValueUnderAnotherKey()
    {
        var value = new MasterKeyUserDataProtector(new FixedStore(_format.Create())).Protect([1, 2, 3]);

        Should.Throw<AuthenticationTagMismatchException>(
            () => new MasterKeyUserDataProtector(new FixedStore(_format.Create())).Unprotect(value));
    }

    [Fact]
    public void ProtectorShouldRejectAnUnknownFormat()
    {
        var protector = new MasterKeyUserDataProtector(new FixedStore(_format.Create()));

        Should.Throw<CryptographicException>(() => protector.Unprotect([2, .. new byte[40]]));
        Should.Throw<CryptographicException>(() => protector.Unprotect([1, 2, 3]));
    }

    [Fact]
    public void ProtectorShouldAskTheStoreOnlyOnce()
    {
        var store = new FixedStore(_format.Create());
        var protector = new MasterKeyUserDataProtector(store);

        protector.Unprotect(protector.Protect([1]));
        protector.Protect([2]);

        store.Calls.ShouldBe(1);
    }

    [Fact]
    public void FileStoreShouldKeepTheKeyItCreated()
    {
        var created = NewFileStore().GetOrCreate();

        NewFileStore().Exists.ShouldBeTrue();
        NewFileStore().GetOrCreate().ShouldBe(created);
    }

    [Fact]
    public void FileStoreShouldBeReadableByItsOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix permissions");
            return;
        }

        NewFileStore().GetOrCreate();

        var file = Path.Combine(_root, "keys", "credential.key");
        File.GetUnixFileMode(file).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.GetUnixFileMode(Path.GetDirectoryName(file)!)
            .ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [Fact]
    public void FileStoreShouldRefuseADamagedKeyRatherThanReplaceIt()
    {
        Directory.CreateDirectory(Path.Combine(_root, "keys"));
        File.WriteAllText(Path.Combine(_root, "keys", "credential.key"), "not a key");

        Should.Throw<MasterKeyUnavailableException>(() => NewFileStore().GetOrCreate());
    }

    [Fact]
    public void ShouldUseTheKeyringWhenItWorks()
    {
        var key = _format.Create();
        var store = new KeyringOrFileMasterKeyStore(new FixedKeyring(key), NewFileStore(), Logs());

        store.GetOrCreate().ShouldBe(key);
        NewFileStore().Exists.ShouldBeFalse();
    }

    [Fact]
    public void ShouldFallBackToTheFileAndStayThereWhenTheKeyringLaterWorks()
    {
        var fallback = new KeyringOrFileMasterKeyStore(new FixedKeyring(null), NewFileStore(), Logs()).GetOrCreate();

        var later = new KeyringOrFileMasterKeyStore(new FixedKeyring(_format.Create()), NewFileStore(), Logs()).GetOrCreate();

        later.ShouldBe(fallback);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FixedStore(byte[] key) : IMasterKeyStore
    {
        public int Calls { get; private set; }

        public byte[] GetOrCreate()
        {
            Calls++;
            return key;
        }
    }

    private sealed class FixedKeyring(byte[]? key) : IKeyringMasterKeyStore
    {
        public byte[]? TryGetOrCreate() => key;
    }
}
