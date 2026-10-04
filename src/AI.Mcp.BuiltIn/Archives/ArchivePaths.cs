namespace AI.Mcp.BuiltIn.Archives;

using System.IO.Compression;

/// <summary>
/// Turns names that come from inside an archive into paths on disk. An archive is untrusted input:
/// an entry name may be rooted (<c>/etc/passwd</c>, <c>C:\Windows\system.ini</c>), may climb out of
/// the destination with <c>..</c>, or may name an NTFS alternate data stream. Every name is re-based
/// under the destination as a purely relative sequence of segments and the result is checked for
/// containment, so no entry can be written outside the directory the caller was granted.
/// </summary>
internal static class ArchivePaths
{
    private static readonly StringComparison Comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>Entry names are reported with forward slashes, whatever the archive stored.</summary>
    public static string Normalize(string name) => name.Replace('\\', '/');

    /// <summary>
    /// True for a name that describes a directory rather than a file. Zip stores a directory as an
    /// entry whose own name is empty (<c>dir/</c>); relying on the trailing separator alone would
    /// miss the ones written with a backslash.
    /// </summary>
    public static bool IsDirectoryEntry(ZipArchiveEntry entry) => entry.Name.Length == 0;

    /// <summary>
    /// Resolves <paramref name="entryName"/> under <paramref name="root"/>, which must already be
    /// canonical, and reports whether the result is contained. A name that is not purely relative,
    /// or that resolves above the root, is refused rather than sanitized: the caller turns it into
    /// an error naming the entry.
    /// </summary>
    public static bool TryResolve(string root, string entryName, out string full)
    {
        full = string.Empty;
        if (string.IsNullOrWhiteSpace(entryName)) return false;
        if (Path.IsPathFullyQualified(entryName) || Path.IsPathRooted(entryName)) return false;

        var segments = Normalize(entryName).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return false;
        foreach (var segment in segments)
        {
            // ".." climbs out; a colon names a drive-relative path or an alternate data stream,
            // neither of which stays inside the destination however it is combined.
            if (segment == "..") return false;
            if (segment.Contains(':')) return false;
        }

        try
        {
            var combined = root;
            foreach (var segment in segments) combined = Path.Combine(combined, segment);
            var canonical = Path.GetFullPath(combined);
            var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            if (!canonical.StartsWith(prefix, Comparison)) return false;
            full = canonical;
            return true;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A name the file system cannot express at all is refused as well: no archive entry
            // is worth an exception escaping the tool.
            return false;
        }
    }
}
