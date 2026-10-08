namespace AI.Contracts.FileSystem;

/// <summary>
/// Path algebra and the path semantics of the platform the paths belong to. Pure string work:
/// nothing here opens, creates or reads anything, so a test can decide which platform's semantics
/// it exercises instead of inheriting the machine it happens to run on.
/// </summary>
/// <remarks>
/// Every comparison — equality, containment, prefix — uses <see cref="Comparison"/>. Nothing here
/// uses the ambient culture, because two paths that name the same file must compare equal and two
/// that merely share a prefix must not.
/// </remarks>
public interface IPath
{
    /// <summary>The separator this platform writes in a path: <c>\</c> on Windows, <c>/</c> elsewhere.</summary>
    char DirectorySeparator { get; }

    /// <summary>The other separator this platform accepts, as in <c>C:/dir</c> on Windows.</summary>
    char AltDirectorySeparator { get; }

    /// <summary>Separates the entries of a path <em>list</em>, as in PATH: <c>;</c> on Windows, <c>:</c> elsewhere.</summary>
    char PathSeparator { get; }

    /// <summary>Characters the platform refuses inside a path segment.</summary>
    char[] InvalidPathChars { get; }

    /// <summary>
    /// How this platform compares two paths: <see cref="StringComparison.OrdinalIgnoreCase"/> on
    /// Windows, <see cref="StringComparison.Ordinal"/> elsewhere.
    /// </summary>
    StringComparison Comparison { get; }

    /// <summary>Whether <see cref="Comparison"/> ignores case.</summary>
    bool IsCaseSensitive { get; }

    /// <summary>
    /// Whether the path names a root, so that no base path is needed to pin it down: the directory
    /// part of <c>C:\a</c> on Windows, <c>/a</c> on Unix. False for <c>a\b</c>.
    /// </summary>
    bool IsFullyQualified(string path);

    /// <summary>
    /// Whether the path starts at a root of <em>some</em> platform, fully qualified or not:
    /// <c>\a</c> is rooted on Windows without naming a drive. Use <see cref="IsFullyQualified"/>
    /// when what matters is whether the path stands on its own.
    /// </summary>
    bool IsRooted(string path);

    /// <summary>Whether the path already ends in a directory separator, in either form.</summary>
    bool EndsInDirectorySeparator(string path);

    /// <summary>
    /// Whether <paramref name="candidate"/> is <paramref name="root"/> itself or lies below it.
    /// </summary>
    /// <param name="recursive">
    /// True accepts any depth below the root; false accepts nothing below the root's own level, so a
    /// grandchild such as <c>a/b/c</c> under <c>a</c> is not inside it.
    /// </param>
    /// <remarks>
    /// The root is inside itself in both modes. Comparison uses <see cref="Comparison"/> and always
    /// ends on a segment boundary, so <c>ab</c> is never inside <c>a</c>. Paths that leave the root
    /// — <c>..</c>, or the root's own parent — are not inside it.
    /// </remarks>
    bool IsInside(string candidate, string root, bool recursive);

    /// <summary>
    /// The canonical form of the path, resolved against the process current directory when it names
    /// no root.
    /// </summary>
    /// <exception cref="ArgumentException">The path is empty or whitespace.</exception>
    string GetFullPath(string path);

    /// <summary>
    /// The canonical form of the path, resolved against <paramref name="basePath"/> when it names no
    /// root. A path that already names a root is canonicalized on its own and the base is ignored.
    /// </summary>
    string GetFullPath(string path, string basePath);

    /// <summary>
    /// Joins two paths with <see cref="DirectorySeparator"/>. An empty part contributes nothing, so
    /// combining with nothing yields the other part unchanged rather than a trailing separator.
    /// </summary>
    string Combine(string first, string second);

    /// <summary>
    /// The path without its last segment, or null when there is nothing above it: a root, or a bare
    /// file name.
    /// </summary>
    string? GetDirectoryName(string path);

    /// <summary>The root of the path (<c>C:\</c>, <c>/</c>), or null when it names none.</summary>
    string? GetPathRoot(string path);

    /// <summary>The last segment of the path.</summary>
    string GetFileName(string path);

    /// <summary>The last segment without its extension.</summary>
    string GetFileNameWithoutExtension(string path);

    /// <summary>The extension of the last segment, including its dot; empty when there is none.</summary>
    string GetExtension(string path);

    /// <summary>
    /// How to get from <paramref name="relativeTo"/> to <paramref name="path"/>, with platform
    /// separators. The same path yields the empty string, and a path outside the base navigates up
    /// with <c>..</c>.
    /// </summary>
    string GetRelativePath(string relativeTo, string path);

    /// <summary>The path with trailing separators removed, except when they are all it has.</summary>
    string TrimEndingDirectorySeparator(string path);
}
