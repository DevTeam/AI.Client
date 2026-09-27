namespace AI.Infrastructure.Credentials;

using System.Security.Cryptography;

/// <summary>A 256-bit AES key, stored as base64.</summary>
public sealed class MasterKeyFormat : IMasterKeyFormat
{
    private const int KeyLength = 32;

    public byte[] Create() => RandomNumberGenerator.GetBytes(KeyLength);

    public string Encode(byte[] key) => Convert.ToBase64String(key);

    public byte[]? TryDecode(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded)) return null;
        try
        {
            var key = Convert.FromBase64String(encoded.Trim());
            return key.Length == KeyLength ? key : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
