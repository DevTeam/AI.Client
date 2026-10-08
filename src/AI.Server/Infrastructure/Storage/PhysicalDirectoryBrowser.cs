namespace AI.Infrastructure.Storage;

using AI.Application.Projects;
using AI.Contracts.FileSystem;

/// <summary>
/// Walks the real file system of the machine the host runs on. Everything here is read-only:
/// the picker needs to know what directories exist, never to change any of them.
/// </summary>
public sealed class PhysicalDirectoryBrowser(IFileSystem files, IPath paths) : IDirectoryBrowser
{
    public Task<DirectoryListing> ListRootsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DirectoryListing(string.Empty, null, true, Roots(), []));
    }

    public async Task<DirectoryListing?> ListAsync(string path, bool includeFiles, CancellationToken cancellationToken)
    {
        var canonical = Canonicalize(path);
        if (canonical.Length == 0)
        {
            return new DirectoryListing(string.Empty, null, true, Roots(), []);
        }

        if (!await files.DirectoryExistsAsync(canonical, cancellationToken))
        {
            return null;
        }

        var parent = ParentOf(canonical);
        try
        {
            // Inaccessible entries are not skipped here: a directory that will not open has to be
            // reported as such rather than shown as an empty one.
            var entries = await files.ListEntriesAsync(canonical,
                new FileEnumerationOptions(SkipInaccessible: false), cancellationToken);
            var directories = new List<DirectoryEntry>();
            var documents = new List<DirectoryEntry>();
            foreach (var entry in entries)
            {
                if (await IsHiddenSystemAsync(entry.Path, cancellationToken)) continue;
                (entry.IsDirectory ? directories : documents).Add(new DirectoryEntry(entry.Name, entry.Path));
            }

            directories.Sort(CompareByName);
            if (includeFiles) documents.Sort(CompareByName);
            return new DirectoryListing(canonical, parent, true, directories, includeFiles ? documents : []);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            // The directory is there, it just will not open for this account — a removed medium,
            // a dropped network share and a denied ACL all land here. Reporting it as missing
            // would send the user hunting for a folder they are looking straight at.
            return new DirectoryListing(canonical, parent, false, [], []);
        }
    }

    public async Task<DirectoryProbe> ResolveAsync(string path, CancellationToken cancellationToken)
    {
        var canonical = Canonicalize(path);
        return new DirectoryProbe(
            canonical,
            canonical.Length > 0 && paths.IsFullyQualified(canonical),
            canonical.Length > 0 && await files.DirectoryExistsAsync(canonical, cancellationToken),
            canonical.Length > 0 && await files.FileExistsAsync(canonical, cancellationToken));
    }

    private static int CompareByName(DirectoryEntry left, DirectoryEntry right) =>
        string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);

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
        if (expanded.Length == 0 || !paths.IsFullyQualified(expanded))
        {
            return expanded;
        }

        try
        {
            var full = paths.GetFullPath(expanded);
            var trimmed = paths.TrimEndingDirectorySeparator(full);
            // "C:" is not "C:\", and "" is not "/": a root loses its meaning without the separator.
            return trimmed.Length == 0 || paths.GetPathRoot(full)?.Length == full.Length ? full : trimmed;
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
    private string? ParentOf(string canonical)
    {
        var parent = paths.GetDirectoryName(canonical);
        return string.IsNullOrEmpty(parent) ? string.Empty : parent;
    }

    /// <summary>
    /// Hides only what is both hidden and system — "$Recycle.Bin", "System Volume Information" and
    /// their kind. A merely hidden directory stays: AppData is hidden, and it is exactly the sort
    /// of place a grant gets pointed at.
    /// </summary>
    private async Task<bool> IsHiddenSystemAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var attributes = await files.GetAttributesAsync(path, cancellationToken);
            return attributes.HasFlag(FileAttributes.Hidden) && attributes.HasFlag(FileAttributes.System);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
