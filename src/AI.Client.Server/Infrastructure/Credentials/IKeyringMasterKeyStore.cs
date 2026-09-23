namespace AI.Client.Infrastructure.Credentials;

/// <summary>The operating system's own secret store: the macOS Keychain or the Linux Secret Service.</summary>
public interface IKeyringMasterKeyStore
{
    /// <returns>The key, or null when the keyring cannot be used on this machine right now.</returns>
    byte[]? TryGetOrCreate();
}
