namespace AI.Infrastructure.Tests.Storage;

using AI.Contracts.FileSystem;

/// <summary>
/// The path semantics one platform uses, as a value: a test picks the platform it is testing
/// instead of inheriting the machine it happens to run on.
/// </summary>
/// <param name="DirectorySeparator">What the platform writes between segments.</param>
/// <param name="AltDirectorySeparator">What it also accepts, as in <c>C:/dir</c>.</param>
/// <param name="PathSeparator">What separates the entries of a path list, as in PATH.</param>
/// <param name="Comparison">How the platform compares two paths.</param>
/// <param name="InvalidPathChars">Characters the platform refuses in a segment.</param>
public sealed record PathSemantics(
    char DirectorySeparator,
    char AltDirectorySeparator,
    char PathSeparator,
    StringComparison Comparison,
    char[] InvalidPathChars)
{
    /// <summary>Windows: backslashes, case-insensitive, drive letters and UNC roots.</summary>
    public static PathSemantics Windows { get; } = new('\\', '/', ';', StringComparison.OrdinalIgnoreCase,
        [.. "\0<>:\"|?*"]);

    /// <summary>Unix: forward slashes, case-sensitive, a single root.</summary>
    public static PathSemantics Unix { get; } = new('/', '/', ':', StringComparison.Ordinal, ['\0']);
}

/// <summary>
/// <see cref="IPath"/> in memory: the same algebra as the platform adapter, with the platform's
/// semantics chosen by the test. Nothing here touches the disk, so a path test asserts the rules
/// rather than the machine it ran on — and a test of the Unix rules can run on Windows.
/// </summary>
/// <remarks>
/// Relative paths are resolved against <see cref="CurrentDirectory"/>, standing in for the process
/// working directory. Both semantics are supported by one implementation because the rules are the
/// same; only the separators, the roots and the comparison differ.
/// </remarks>
public sealed class InMemoryPath : IPath
{
    public InMemoryPath(PathSemantics semantics, string currentDirectory)
    {
        Semantics = semantics;
        CurrentDirectory = TrimEndingDirectorySeparator(currentDirectory);
        Separators = [semantics.DirectorySeparator, semantics.AltDirectorySeparator];
        if (semantics.Comparison == StringComparison.Ordinal) CaseSensitiveComparer = StringComparer.Ordinal;
        else CaseSensitiveComparer = StringComparer.OrdinalIgnoreCase;
    }

    /// <summary>The platform whose rules this instance follows.</summary>
    public PathSemantics Semantics { get; }

    /// <summary>What a relative path is resolved against.</summary>
    public string CurrentDirectory { get; }

    public char DirectorySeparator => Semantics.DirectorySeparator;

    public char AltDirectorySeparator => Semantics.AltDirectorySeparator;

    public char PathSeparator => Semantics.PathSeparator;

    public char[] InvalidPathChars => Semantics.InvalidPathChars;

    public StringComparison Comparison => Semantics.Comparison;

    public bool IsCaseSensitive => Semantics.Comparison == StringComparison.Ordinal;

    private char[] Separators { get; }

    private StringComparer CaseSensitiveComparer { get; }

    public bool IsRooted(string path) => path.Length > 0 && (Separators.Contains(path[0])
        || (IsWindows && path.Length > 1 && path[1] == ':'));

    /// <summary>
    /// Fully qualified means the path needs no base to be pinned down: a drive and a separator on
    /// Windows, the leading separator on Unix. <c>a\b</c> and <c>\a</c> are rooted without being
    /// fully qualified, which is the distinction that decides whether a stored path stands alone.
    /// </summary>
    public bool IsFullyQualified(string path)
    {
        if (path.Length == 0) return false;
        if (!IsWindows) return path[0] == DirectorySeparator;
        if (path.StartsWith(new string(DirectorySeparator, 2), StringComparison.Ordinal))
            return path.Length > 2 && !Separators.Contains(path[2]);
        if (path.Length >= 3 && path[1] == ':' && Separators.Contains(path[2])) return true;
        return path.Length >= 2 && path[1] == ':' && Separators.Contains(path[2]);
    }

    public bool EndsInDirectorySeparator(string path) => path.Length > 0 && Separators.Contains(path[^1]);

    public bool IsInside(string candidate, string root, bool recursive)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(root)) return false;
        var target = TrimEndingDirectorySeparator(GetFullPath(candidate));
        var boundary = TrimEndingDirectorySeparator(GetFullPath(root));
        if (CaseSensitiveComparer.Equals(target, boundary)) return true;
        if (!target.StartsWith(boundary, Comparison)) return false;

        // The remainder decides it: "a" must not match "ab", so what follows the root has to begin
        // with a separator, and with recursive off nothing may follow beyond one segment.
        var remainder = target[boundary.Length..];
        if (remainder.Length == 0) return true;
        if (!Separators.Contains(remainder[0])) return false;
        var below = remainder.TrimStart(Separators);
        if (below.Length == 0) return true;
        if (below.Split(Separators).Any(segment => segment == "..")) return false;
        return recursive || !below.ContainsAny(Separators);
    }

    public string GetFullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A path cannot be empty or whitespace.", nameof(path));
        return IsFullyQualified(path) ? Normalize(path) : Normalize(Combine(CurrentDirectory, path));
    }

    public string GetFullPath(string path, string basePath)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A path cannot be empty or whitespace.", nameof(path));
        return IsFullyQualified(path) ? Normalize(path) : Normalize(Combine(basePath, path));
    }

    /// <summary>
    /// Joins two paths. An empty part contributes nothing, so combining with nothing yields the
    /// other part unchanged rather than a stray separator — which is what a caller writing
    /// <c>Combine(directory, name)</c> means when either side happens to be empty.
    /// </summary>
    public string Combine(string first, string second)
    {
        if (first.Length == 0) return second;
        if (second.Length == 0) return first;
        if (IsRooted(second)) return second;
        return EndsInDirectorySeparator(first) ? first + second : first + DirectorySeparator + second;
    }

    /// <summary>
    /// The path without its last segment. A root and a bare name have nothing above them, and the
    /// segment above a path directly under a root <em>is</em> that root: <c>C:\data</c> answers
    /// <c>C:\</c>, not the drive-relative <c>C:</c> that names nothing a caller can open. The upward
    /// walk a write performs relies on that, because the root's own directory is null and so ends
    /// the walk by itself.
    /// </summary>
    public string? GetDirectoryName(string path)
    {
        if (path.Length == 0) return null;
        var normalized = Normalize(path);
        var root = GetPathRoot(normalized);
        if (CaseSensitiveComparer.Equals(normalized, root ?? string.Empty)) return null;
        var cut = normalized.TrimEnd(Separators).LastIndexOfAny(Separators);
        if (cut < 0) return null;
        var directory = normalized[..cut];
        // The cut can land on the leading separator or on the drive, and neither is a directory: an
        // empty prefix is the root's own separator and "C:" is a drive with no directory on it. What
        // a caller asking for the directory above "C:\data" means is the root, so answer that.
        if (directory.Length == 0 || CaseSensitiveComparer.Equals(directory, (root ?? string.Empty).TrimEnd(Separators)))
            return root;
        return directory;
    }

    public string? GetPathRoot(string path)
    {
        if (path.Length == 0) return null;
        if (!IsWindows) return path[0] == DirectorySeparator ? DirectorySeparator.ToString() : null;
        if (path.StartsWith(new string(DirectorySeparator, 2), StringComparison.Ordinal))
        {
            // UNC: \\server\share is the root; anything shorter names no reachable root.
            var segments = path[2..].Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            return segments.Length >= 2
                ? new string(DirectorySeparator, 2) + segments[0] + DirectorySeparator + segments[1]
                : null;
        }

        if (path.Length >= 2 && path[1] == ':') return path[..2] + DirectorySeparator;
        return Separators.Contains(path[0]) ? DirectorySeparator.ToString() : null;
    }

    public string GetFileName(string path)
    {
        var cut = path.TrimEnd(Separators).LastIndexOfAny(Separators);
        return cut < 0 ? path : path[(cut + 1)..];
    }

    public string GetFileNameWithoutExtension(string path)
    {
        var name = GetFileName(path);
        var extension = GetExtension(name);
        return extension.Length == 0 ? name : name[..^extension.Length];
    }

    public string GetExtension(string path)
    {
        var name = GetFileName(path);
        var dot = name.LastIndexOf('.');
        // A name that starts with the dot has no extension: ".gitignore" is a name, not a suffix.
        return dot <= 0 ? string.Empty : name[dot..];
    }

    /// <summary>
    /// How to get from one path to another: the same path needs no travel, and a path that is not
    /// below the base climbs out of it with <c>..</c> first.
    /// </summary>
    public string GetRelativePath(string relativeTo, string path)
    {
        var from = TrimEndingDirectorySeparator(GetFullPath(relativeTo)).Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var to = TrimEndingDirectorySeparator(GetFullPath(path)).Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var shared = 0;
        while (shared < from.Length && shared < to.Length && CaseSensitiveComparer.Equals(from[shared], to[shared])) shared++;
        var segments = Enumerable.Repeat("..", from.Length - shared).Concat(to[shared..]);
        return string.Join(DirectorySeparator, segments);
    }

    /// <summary>Removes trailing separators, except when they are all the path is: a root stays a root.</summary>
    public string TrimEndingDirectorySeparator(string path)
    {
        var trimmed = path.TrimEnd(Separators);
        if (trimmed.Length > 0)
        {
            // A drive on its own has to keep its separator: "C:" is a relative path, "C:\" is a root.
            if (trimmed.Length >= 2 && trimmed[1] == ':' && trimmed.Length == 2) return trimmed + DirectorySeparator;
            return trimmed;
        }

        return path.Length > 0 ? DirectorySeparator.ToString() : path;
    }

    private bool IsWindows => DirectorySeparator == '\\';

    /// <summary>
    /// Folds <c>.</c> and <c>..</c> into the path and writes it with the platform separator. A
    /// segment that would climb above the root is dropped, which is the rule every platform uses:
    /// <c>a/../..</c> is the root, not one level above it.
    /// </summary>
    private string Normalize(string path)
    {
        var root = GetPathRoot(path) ?? string.Empty;
        // A root can be longer than the path it came from — "C:" has the root "C:\" — and slicing
        // there would throw. Such a path has no root to keep, so it is treated as a plain name.
        if (root.Length > path.Length) root = string.Empty;
        var remainder = path[root.Length..];
        var segments = new List<string>();
        foreach (var segment in remainder.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        var body = string.Join(DirectorySeparator, segments);
        if (root.Length == 0) return body.Length == 0 ? "." : body;
        if (body.Length == 0) return root;
        return EndsInDirectorySeparator(root) ? root + body : root + DirectorySeparator + body;
    }
}
