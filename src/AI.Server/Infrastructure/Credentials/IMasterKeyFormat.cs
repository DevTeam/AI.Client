namespace AI.Infrastructure.Credentials;

/// <summary>How a master key is made and written down, the same way in every store.</summary>
public interface IMasterKeyFormat
{
    byte[] Create();

    string Encode(byte[] key);

    /// <returns>The key, or null when <paramref name="encoded"/> is not one.</returns>
    byte[]? TryDecode(string? encoded);
}
