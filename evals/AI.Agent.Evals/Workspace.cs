namespace AI.Agent.Evals;

using System.Security.Cryptography;

/// <summary>The files of an attempt's workspace by relative path and content hash, build output left out.</summary>
public sealed class Workspace
{
    private static readonly string[] Ignored = [".git", "bin", "obj", ".vs", "node_modules", "__pycache__", "TestResults"];
    private readonly Dictionary<string, string> _hashes;

    private Workspace(string root, Dictionary<string, string> hashes)
    {
        Root = root;
        _hashes = hashes;
    }

    public string Root { get; }

    public static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
    }

    public static Workspace Snapshot(string root) => new(root, Directory.Exists(root)
        ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => (Path: Relative(root, file), File: file))
            .Where(item => !item.Path.Split('/').Any(part => Ignored.Contains(part, StringComparer.OrdinalIgnoreCase)))
            .ToDictionary(item => item.Path, item => Hash(item.File), StringComparer.Ordinal)
        : []);

    public IEnumerable<string> Files => _hashes.Keys;

    public bool Has(string path) => _hashes.ContainsKey(path);

    public bool SameAs(Workspace before, string path) =>
        _hashes.TryGetValue(path, out var hash) && before._hashes.TryGetValue(path, out var old) && hash == old;

    /// <summary>What changed since <paramref name="before"/>: "A path", "M path" or "D path".</summary>
    public IReadOnlyList<string> ChangesSince(Workspace before) =>
        _hashes.Keys.Union(before._hashes.Keys).Order(StringComparer.Ordinal)
            .Select(path => (before._hashes.TryGetValue(path, out var old), _hashes.TryGetValue(path, out var now)) switch
            {
                (false, true) => "A " + path,
                (true, false) => "D " + path,
                _ when before._hashes[path] != _hashes[path] => "M " + path,
                _ => null
            })
            .OfType<string>().ToArray();

    public static string Relative(string root, string file) => Path.GetRelativePath(root, file).Replace('\\', '/');

    private static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
