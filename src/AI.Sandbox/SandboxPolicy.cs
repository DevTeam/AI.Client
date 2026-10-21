namespace AI.Sandbox;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>What a confined process may do with one path and everything below it.</summary>
public enum SandboxAccess
{
    /// <summary>List, read and execute.</summary>
    Read,

    /// <summary>Everything <see cref="Read"/> allows, plus create, write, rename and delete.</summary>
    ReadWrite
}

/// <summary>One file or directory the policy opens, with what may be done there.</summary>
public sealed record SandboxPath(string Path, SandboxAccess Access);

/// <summary>
/// The file system a confined process may reach: the listed paths, and the read-only system
/// locations a program needs to start at all (the OS libraries, the .NET runtime, the program's own
/// directory). Everything else is out of reach as far as the platform enforces it, which is what
/// <see cref="SandboxSupport"/> reports.
/// </summary>
/// <param name="Paths">Absolute paths; a path that does not exist is skipped.</param>
/// <param name="AllowNetwork">Whether the process may open network connections.</param>
public sealed record SandboxPolicy(IReadOnlyList<SandboxPath> Paths, bool AllowNetwork = true)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// The policy with every path made absolute and normalized, duplicates merged to the widest
    /// access. A launcher compares and grants paths, so two spellings of one directory must agree.
    /// </summary>
    public SandboxPolicy Normalize()
    {
        var paths = new Dictionary<string, SandboxAccess>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var entry in Paths)
        {
            if (string.IsNullOrWhiteSpace(entry.Path) || !System.IO.Path.IsPathFullyQualified(entry.Path))
            {
                throw new ArgumentException($"Sandbox paths must be absolute: '{entry.Path}'.", nameof(Paths));
            }

            var full = System.IO.Path.GetFullPath(entry.Path);
            var path = System.IO.Path.GetPathRoot(full) == full ? full : System.IO.Path.TrimEndingDirectorySeparator(full);
            paths[path] = paths.TryGetValue(path, out var known) && known == SandboxAccess.ReadWrite ? known : entry.Access;
        }

        return this with { Paths = paths.Select(pair => new SandboxPath(pair.Key, pair.Value)).ToArray() };
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static SandboxPolicy FromJson(string json) =>
        JsonSerializer.Deserialize<SandboxPolicy>(json, Json) ?? throw new JsonException("The sandbox policy is empty.");
}
