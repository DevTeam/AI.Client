namespace AI.Server.Hosting;

using System.Security.Cryptography;
using Infrastructure.Storage;

/// <summary>Stores only hashes of grants issued to browsers of the public Web application.</summary>
public sealed class BrowserAccessService(IProjectStorageLocation location) : IBrowserAccessService
{
    private readonly Lock _sync = new();
    private readonly string _path = Path.Combine(location.RootDirectory, "browser-access.txt");
    private readonly HashSet<string> _hashes = Load(location.RootDirectory);

    public string Grant()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        lock (_sync)
        {
            _hashes.Add(Hash(token));
            Save();
        }

        return token;
    }

    public bool Allows(string? token)
    {
        if (token is not { Length: 64 } || !token.All(Uri.IsHexDigit)) return false;
        lock (_sync) return _hashes.Contains(Hash(token));
    }

    public bool Revoke(string? token)
    {
        if (token is not { Length: 64 } || !token.All(Uri.IsHexDigit)) return false;
        lock (_sync)
        {
            if (!_hashes.Remove(Hash(token))) return false;
            Save();
            return true;
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(location.RootDirectory);
        var temporary = _path + ".new";
        File.WriteAllLines(temporary, _hashes.Order(StringComparer.Ordinal));
        File.Move(temporary, _path, true);
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token)));

    private static HashSet<string> Load(string directory)
    {
        var path = Path.Combine(directory, "browser-access.txt");
        return File.Exists(path)
            ? File.ReadAllLines(path).Where(line => line.Length == 64 && line.All(Uri.IsHexDigit))
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }
}
