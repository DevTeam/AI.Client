namespace AI.Server.Hosting;

using System.Security.Cryptography;
using AI.Application.Projects;
using Infrastructure.Storage;

/// <summary>Stores only hashes of grants issued to browsers of the public Web application.</summary>
public sealed class BrowserAccessService(IProjectStorageLocation location, IClock clock) : IBrowserAccessService
{
    // Long enough to open a browser and load the Web app on a slow machine, short enough that a
    // code left in a browser's history is useless by the time anyone could read it.
    private static readonly TimeSpan PairingCodeLifetime = TimeSpan.FromMinutes(2);

    private readonly Lock _sync = new();
    private readonly string _path = Path.Combine(location.RootDirectory, "browser-access.txt");
    private readonly HashSet<string> _hashes = Load(location.RootDirectory);
    private readonly Dictionary<string, DateTimeOffset> _pairingCodes = new(StringComparer.Ordinal);

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
        if (!ValidToken(token)) return false;
        lock (_sync) return _hashes.Contains(Hash(token!));
    }

    public bool Revoke(string? token)
    {
        if (!ValidToken(token)) return false;
        lock (_sync)
        {
            if (!_hashes.Remove(Hash(token!))) return false;
            Save();
            return true;
        }
    }

    public string CreatePairingCode()
    {
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        lock (_sync)
        {
            RemoveExpiredPairingCodes();
            _pairingCodes[Hash(code)] = clock.UtcNow + PairingCodeLifetime;
        }

        return code;
    }

    public string? RedeemPairingCode(string? code)
    {
        if (code is not { Length: 32 } || !code.All(Uri.IsHexDigit)) return null;
        lock (_sync)
        {
            RemoveExpiredPairingCodes();
            if (!_pairingCodes.Remove(Hash(code.ToUpperInvariant()))) return null;
        }

        return Grant();
    }

    private void RemoveExpiredPairingCodes()
    {
        var now = clock.UtcNow;
        foreach (var expired in _pairingCodes.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToList())
            _pairingCodes.Remove(expired);
    }

    private void Save()
    {
        Directory.CreateDirectory(location.RootDirectory);
        var temporary = _path + ".new";
        File.WriteAllLines(temporary, _hashes.Order(StringComparer.Ordinal));
        File.Move(temporary, _path, true);
    }

    private static bool ValidToken(string? token) => token is { Length: 64 } && token.All(Uri.IsHexDigit);

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
