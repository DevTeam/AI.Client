namespace AI.Infrastructure.Credentials;

using System.Security.Cryptography;

/// <summary>
/// AES-256-GCM under the key from <see cref="IMasterKeyStore"/>: the macOS and Linux counterpart of
/// DPAPI. The layout is <c>version | nonce | tag | ciphertext</c>, and a tampered or foreign blob
/// fails authentication instead of decrypting to garbage.
/// </summary>
public sealed class MasterKeyUserDataProtector : IUserDataProtector
{
    private const byte Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 1 + NonceSize + TagSize;
    private readonly Lazy<byte[]> _key;
    private readonly byte[] _associatedData = [.. "AI.EndpointCredential.v1"u8];

    public MasterKeyUserDataProtector(IMasterKeyStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        // Looked up once: the keyring is a process launch away, and the key does not change.
        _key = new Lazy<byte[]>(store.GetOrCreate, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public byte[] Protect(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var result = new byte[HeaderSize + data.Length];
        result[0] = Version;
        var nonce = result.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key.Value, TagSize);
        aes.Encrypt(nonce, data, result.AsSpan(HeaderSize), result.AsSpan(1 + NonceSize, TagSize), _associatedData);
        return result;
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        ArgumentNullException.ThrowIfNull(protectedData);
        if (protectedData.Length < HeaderSize || protectedData[0] != Version)
        {
            throw new CryptographicException("The protected value is not in a format this version can read.");
        }

        var result = new byte[protectedData.Length - HeaderSize];
        using var aes = new AesGcm(_key.Value, TagSize);
        aes.Decrypt(
            protectedData.AsSpan(1, NonceSize),
            protectedData.AsSpan(HeaderSize),
            protectedData.AsSpan(1 + NonceSize, TagSize),
            result,
            _associatedData);
        return result;
    }
}
