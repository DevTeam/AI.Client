namespace AI.Server.Hosting;

using System.Security.Cryptography;
using AI.Application.Projects;
using AI.Contracts.FileSystem;
using Infrastructure.Storage;

/// <summary>Stores only hashes of grants issued to browsers of the public Web application.</summary>
public sealed class BrowserAccessService : IBrowserAccessService
{
    // Long enough to open a browser and load the Web app on a slow machine, short enough that a
    // code left in a browser's history is useless by the time anyone could read it.
    private static readonly TimeSpan PairingCodeLifetime = TimeSpan.FromMinutes(2);

    private readonly Lock _sync = new();
    private readonly IFileSystem _files;
    private readonly IAtomicFileWriter _atomic;
    private readonly IClock _clock;
    private readonly string _path;
    private readonly HashSet<string> _hashes;
    private readonly Dictionary<string, DateTimeOffset> _pairingCodes = new(StringComparer.Ordinal);

    public BrowserAccessService(IProjectStorageLocation location, IClock clock, IFileSystem files,
        IAtomicFileWriter atomic)
    {
        _clock = clock;
        _files = files;
        _atomic = atomic;
        _path = Path.Combine(location.RootDirectory, "browser-access.txt");
        _hashes = Load(location.RootDirectory);
    }

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
            _pairingCodes[Hash(code)] = _clock.UtcNow + PairingCodeLifetime;
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
        var now = _clock.UtcNow;
        foreach (var expired in _pairingCodes.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToList())
            _pairingCodes.Remove(expired);
    }

    private void Save()
    {
        // The grant list is read and written from synchronous members, so the contract is awaited
        // here; a write of a few hashes is bounded and local.
        Wait(_files.CreateDirectoryAsync(Path.GetDirectoryName(_path)!, ownerOnly: false, CancellationToken.None));
        Wait(_atomic.WriteTextAsync(_path,
            string.Join(Environment.NewLine, _hashes.Order(StringComparer.Ordinal)) + Environment.NewLine,
            CancellationToken.None));
    }

    private static bool ValidToken(string? token) => token is { Length: 64 } && token.All(Uri.IsHexDigit);

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token)));

    private HashSet<string> Load(string directory)
    {
        var path = Path.Combine(directory, "browser-access.txt");
        if (!Wait(_files.FileExistsAsync(path, CancellationToken.None)))
            return new HashSet<string>(StringComparer.Ordinal);
        var content = Wait(_files.ReadTextAsync(path, CancellationToken.None));
        return (content ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length == 64 && line.All(Uri.IsHexDigit))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static T Wait<T>(Task<T> operation) => operation.GetAwaiter().GetResult();

    private static void Wait(Task operation) => operation.GetAwaiter().GetResult();
}
