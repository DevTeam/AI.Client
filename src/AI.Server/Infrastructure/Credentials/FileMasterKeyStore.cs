namespace AI.Infrastructure.Credentials;

using Storage;

/// <summary>
/// The key in <c>&lt;data&gt;/keys/credential.key</c>, readable by this user only. The fallback for a
/// machine without a working keyring: weaker than the OS store, because anything running as this
/// user can read the file, but it keeps credentials working instead of refusing them.
/// </summary>
public sealed class FileMasterKeyStore(IProjectStorageLocation location, IMasterKeyFormat format) : IFileMasterKeyStore
{
    private string KeyDirectory => Path.Combine(location.RootDirectory, "keys");

    private string KeyPath => Path.Combine(KeyDirectory, "credential.key");

    public bool Exists => File.Exists(KeyPath);

    public byte[] GetOrCreate()
    {
        if (Exists)
        {
            return format.TryDecode(File.ReadAllText(KeyPath))
                   ?? throw new MasterKeyUnavailableException($"The credential key file '{KeyPath}' is damaged.");
        }

        var key = format.Create();
        var directory = Directory.CreateDirectory(KeyDirectory);
        if (!OperatingSystem.IsWindows())
        {
            directory.UnixFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        }

        // Written to a temporary file first and moved into place, so a crash can leave no key file
        // at all but never a half-written one that would lock the credentials out.
        var temporary = KeyPath + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var writer = new StreamWriter(new FileStream(temporary, options)))
        {
            writer.Write(format.Encode(key));
        }

        File.Move(temporary, KeyPath, overwrite: false);
        return key;
    }
}
