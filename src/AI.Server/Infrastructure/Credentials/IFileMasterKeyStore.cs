namespace AI.Infrastructure.Credentials;

/// <summary>The file-based key store, which can tell whether it has ever been used.</summary>
public interface IFileMasterKeyStore : IMasterKeyStore
{
    bool Exists { get; }
}
