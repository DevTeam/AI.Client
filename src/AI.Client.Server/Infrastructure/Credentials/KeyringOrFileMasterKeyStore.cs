namespace AI.Client.Infrastructure.Credentials;

using Microsoft.Extensions.Logging;

/// <summary>
/// The keyring when it works, the key file when it does not — and never a switch between the two
/// once a choice is made. A key that changed between runs would leave every stored credential
/// undecryptable, which is worse than either store on its own.
/// </summary>
public sealed class KeyringOrFileMasterKeyStore(
    IKeyringMasterKeyStore keyring,
    IFileMasterKeyStore file,
    ILoggerProvider logs) : IMasterKeyStore
{
    private readonly ILogger _logger = logs.CreateLogger(typeof(KeyringOrFileMasterKeyStore).FullName!);

    public byte[] GetOrCreate()
    {
        // Once the file holds the key it stays the key, even if a keyring appears later.
        if (file.Exists) return file.GetOrCreate();
        if (keyring.TryGetOrCreate() is { } key) return key;

#pragma warning disable CA1848 // Called once per process; a LoggerMessage delegate buys nothing here.
        _logger.LogWarning(
            "The system keyring is not available; credentials are encrypted with a key kept in the data directory instead.");
#pragma warning restore CA1848
        return file.GetOrCreate();
    }
}
