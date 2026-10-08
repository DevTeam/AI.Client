namespace AI.Infrastructure.Credentials;

using AI.Contracts.FileSystem;
using Storage;

/// <summary>
/// The key in <c>&lt;data&gt;/keys/credential.key</c>, readable by this user only. The fallback for a
/// machine without a working keyring: weaker than the OS store, because anything running as this
/// user can read the file, but it keeps credentials working instead of refusing them.
/// </summary>
public sealed class FileMasterKeyStore(IProjectStorageLocation location, IMasterKeyFormat format, IFileSystem files)
    : IFileMasterKeyStore
{
    private string KeyDirectory => Path.Combine(location.RootDirectory, "keys");

    private string KeyPath => Path.Combine(KeyDirectory, "credential.key");

    public bool Exists => files.FileExistsAsync(KeyPath, CancellationToken.None).GetAwaiter().GetResult();

    public byte[] GetOrCreate()
    {
        if (Exists)
        {
            return format.TryDecode(files.ReadTextAsync(KeyPath, CancellationToken.None).GetAwaiter().GetResult())
                   ?? throw new MasterKeyUnavailableException($"The credential key file '{KeyPath}' is damaged.");
        }

        var key = format.Create();
        // Owner-only is the mode this directory has always been created with, now asked for through
        // the contract instead of set afterwards: the contract's flag is that same POSIX mode 700.
        files.CreateDirectoryAsync(KeyDirectory, ownerOnly: true, CancellationToken.None).GetAwaiter().GetResult();

        // Written to a temporary file first and moved into place, so a crash can leave no key file
        // at all but never a half-written one that would lock the credentials out.
        var temporary = KeyPath + ".tmp";
        // The POSIX creation mode of the key file is the documented exception the frozen contract
        // has no member for: it is set here, on the platform call itself.
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var writer = new StreamWriter(new FileStream(temporary, options)))
        {
            writer.Write(format.Encode(key));
        }

        // Never clobbering an existing key is the point of the move: a second writer must fail here
        // rather than replace a key the credentials were already encrypted with.
        files.MoveAsync(temporary, KeyPath, overwrite: false, CancellationToken.None).GetAwaiter().GetResult();
        return key;
    }
}
