namespace AI.Client.Infrastructure.Credentials;

/// <summary>
/// Keeps the one key that encrypts stored credentials where only this user can read it. Used on
/// macOS and Linux; Windows encrypts with DPAPI instead.
/// </summary>
public interface IMasterKeyStore
{
    /// <summary>The key, created and stored on first use.</summary>
    /// <exception cref="MasterKeyUnavailableException">The store cannot be read or written.</exception>
    byte[] GetOrCreate();
}
