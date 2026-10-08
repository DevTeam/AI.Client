namespace AI.Contracts.FileSystem;

/// <summary>
/// <see cref="IPath"/> on the platform this build runs on. The only path algebra in the product
/// that is allowed to ask the operating system which platform it is; everything else is handed one
/// of these or a fake, so no other type's behavior depends on where the tests happen to run.
/// </summary>
public sealed class SystemPath : IPath
{
    private static readonly char[] WindowsInvalidChars =
        Path.GetInvalidPathChars().Concat(['<', '>', ':', '"', '|', '?', '*']).Distinct().ToArray();

    private static readonly char[] UnixInvalidChars = Path.GetInvalidPathChars().Concat(['\0']).Distinct().ToArray();

    private static readonly char[] WindowsSeparators = ['\\', '/'];
    private static readonly char[] UnixSeparators = ['/'];

    public char DirectorySeparator => Path.DirectorySeparatorChar;

    public char AltDirectorySeparator => Path.AltDirectorySeparatorChar;

    public char PathSeparator => Path.PathSeparator;

    public char[] InvalidPathChars => OperatingSystem.IsWindows() ? WindowsInvalidChars : UnixInvalidChars;

    /// <summary>
    /// Windows paths are not case-sensitive, so two spellings of one path must compare equal there;
    /// elsewhere they are different paths and must not.
    /// </summary>
    public StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public bool IsCaseSensitive => Comparison == StringComparison.Ordinal;

    private static char[] SeparatorsFor(bool windows) => windows ? WindowsSeparators : UnixSeparators;

    public bool IsFullyQualified(string path) => Path.IsPathFullyQualified(path);

    public bool IsRooted(string path) => Path.IsPathRooted(path);

    public bool EndsInDirectorySeparator(string path) => Path.EndsInDirectorySeparator(path);

    /// <summary>
    /// Containment as a segment-wise walk rather than a string prefix test, because "ab" starts with
    /// "a" but is not inside it. Both paths are canonicalized first, so "a/./b" and "a/b" agree, and
    /// every comparison uses <see cref="Comparison"/>: on Windows the check is case-insensitive,
    /// elsewhere exact.
    /// </summary>
    public bool IsInside(string candidate, string root, bool recursive)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(root)) return false;
        var windows = OperatingSystem.IsWindows();
        var separators = SeparatorsFor(windows);
        var target = TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var boundary = TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (target.Equals(boundary, Comparison)) return true;
        if (!target.StartsWith(boundary, Comparison)) return false;

        // The remainder decides it: "a" must not match "ab", so what follows the root has to start
        // with a separator, and with recursive off nothing may follow but one segment.
        var remainder = target[boundary.Length..];
        if (remainder.Length == 0) return true;
        if (!separators.Contains(remainder[0])) return false;
        var below = remainder.TrimStart(separators);
        if (below.Length == 0) return true;
        if (below.Split(separators).Any(segment => segment == "..")) return false;
        return recursive || !below.ContainsAny(separators);
    }

    public string GetFullPath(string path) => string.IsNullOrWhiteSpace(path)
        ? throw new ArgumentException("A path cannot be empty or whitespace.", nameof(path))
        : Path.GetFullPath(path);

    public string GetFullPath(string path, string basePath) => string.IsNullOrWhiteSpace(path)
        ? throw new ArgumentException("A path cannot be empty or whitespace.", nameof(path))
        : Path.GetFullPath(path, basePath);

    public string Combine(string first, string second) => first.Length == 0 ? second
        : second.Length == 0 ? first
        : Path.Combine(first, second);

    public string? GetDirectoryName(string path) => Path.GetDirectoryName(path);

    public string? GetPathRoot(string path) => Path.GetPathRoot(path);

    public string GetFileName(string path) => Path.GetFileName(path);

    public string GetFileNameWithoutExtension(string path) => Path.GetFileNameWithoutExtension(path);

    public string GetExtension(string path) => Path.GetExtension(path);

    public string GetRelativePath(string relativeTo, string path) => Path.GetRelativePath(relativeTo, path);

    public string TrimEndingDirectorySeparator(string path) => Path.TrimEndingDirectorySeparator(path);
}
