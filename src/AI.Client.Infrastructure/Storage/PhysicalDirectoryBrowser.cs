namespace AI.Client.Infrastructure.Storage;

using AI.Client.Application.Projects;
using AI.Client.Contracts.FileSystem;

/// <summary>
/// Walks the real file system of the machine the host runs on. Everything here is read-only:
/// the picker needs to know what directories exist, never to change any of them.
/// </summary>
public sealed class PhysicalDirectoryBrowser : IDirectoryBrowser
{
    public Task<DirectoryListing> ListRootsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DirectoryListing(string.Empty, null, true, Roots(), []));
    }

    public Task<DirectoryListing?> ListAsync(string path, bool includeFiles, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var canonical = Canonicalize(path);
        if (canonical.Length == 0)
        {
            return Task.FromResult<DirectoryListing?>(new DirectoryListing(string.Empty, null, true, Roots(), []));
        }

        if (!Directory.Exists(canonical))
        {
            return Task.FromResult<DirectoryListing?>(null);
        }

        var parent = ParentOf(canonical);
        try
        {
            var directory = new DirectoryInfo(canonical);
            var children = directory
                .EnumerateDirectories()
                .Where(item => !IsHiddenSystem(item))
                .Select(item => new DirectoryEntry(item.Name, item.FullName))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var files = includeFiles
                ? directory
                    .EnumerateFiles()
                    .Where(item => !IsHiddenSystem(item))
                    .Select(item => new DirectoryEntry(item.Name, item.FullName))
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];
            return Task.FromResult<DirectoryListing?>(new DirectoryListing(canonical, parent, true, children, files));
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            // The directory is there, it just will not open for this account — a removed medium,
            // a dropped network share and a denied ACL all land here. Reporting it as missing
            // would send the user hunting for a folder they are looking straight at.
            return Task.FromResult<DirectoryListing?>(new DirectoryListing(canonical, parent, false, [], []));
        }
    }

    public Task<DirectoryProbe> ResolveAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var canonical = Canonicalize(path);
        return Task.FromResult(new DirectoryProbe(
            canonical,
            canonical.Length > 0 && Path.IsPathFullyQualified(canonical),
            canonical.Length > 0 && Directory.Exists(canonical),
            canonical.Length > 0 && File.Exists(canonical)));
    }

    /// <summary>
    /// Environment variables and a leading "~" are expanded because they are what people paste,
    /// then the path is resolved and stripped of its trailing separator so that one directory has
    /// one spelling. A path that is not fully qualified is left exactly as typed: resolving it
    /// would silently anchor it to the host process's working directory, which is a place the user
    /// never named and cannot see.
    /// </summary>
    public string Canonicalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var expanded = Expand(path.Trim());
        if (expanded.Length == 0 || !Path.IsPathFullyQualified(expanded))
        {
            return expanded;
        }

        try
        {
            var full = Path.GetFullPath(expanded);
            var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // "C:" is not "C:\", and "" is not "/": a root loses its meaning without the separator.
            return trimmed.Length == 0 || Path.GetPathRoot(full)?.Length == full.Length ? full : trimmed;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return expanded;
        }
    }

    private static string Expand(string path)
    {
        if (path.Length == 0)
        {
            return path;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (expanded == "~" || expanded.StartsWith("~/", StringComparison.Ordinal) || expanded.StartsWith("~\\", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            expanded = home.Length == 0 ? expanded : home + expanded[1..];
        }

        return expanded;
    }

    /// <summary>
    /// The roots level. On Windows these are the ready drives; elsewhere there is exactly one root
    /// and every mount hangs below it, so listing mount points as siblings of "/" would invent a
    /// level the file system does not have.
    /// </summary>
    private static List<DirectoryEntry> Roots()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [new DirectoryEntry("/", "/")];
        }

        var roots = new List<DirectoryEntry>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            // IsReady throws for a drive that disappeared between the listing and the question.
            try
            {
                if (!drive.IsReady) continue;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var label = string.IsNullOrWhiteSpace(SafeLabel(drive))
                ? drive.Name
                : $"{drive.Name} ({SafeLabel(drive)})";
            roots.Add(new DirectoryEntry(label, drive.Name));
        }

        return roots;
    }

    private static string SafeLabel(DriveInfo drive)
    {
        try
        {
            return drive.VolumeLabel;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <summary>Where "up" leads. A root's parent is the roots level, which is the empty path.</summary>
    private static string? ParentOf(string canonical)
    {
        var parent = Path.GetDirectoryName(canonical);
        return string.IsNullOrEmpty(parent) ? string.Empty : parent;
    }

    /// <summary>
    /// Hides only what is both hidden and system — "$Recycle.Bin", "System Volume Information" and
    /// their kind. A merely hidden directory stays: AppData is hidden, and it is exactly the sort
    /// of place a grant gets pointed at.
    /// </summary>
    private static bool IsHiddenSystem(FileSystemInfo entry)
    {
        try
        {
            return entry.Attributes.HasFlag(FileAttributes.Hidden)
                && entry.Attributes.HasFlag(FileAttributes.System);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
