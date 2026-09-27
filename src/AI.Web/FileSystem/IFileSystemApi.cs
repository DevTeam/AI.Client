namespace AI.Web.FileSystem;

using AI.Contracts.FileSystem;

/// <summary>
/// Reads the host's file system so a directory grant can be picked. Every method returns null when
/// the host has nothing to offer — the directory is gone, or file-system browsing is switched off
/// there entirely — because the picker has the same answer for both: fall back to typing a path.
/// </summary>
public interface IFileSystemApi
{
    Task<DirectoryListing?> ListRootsAsync(CancellationToken cancellationToken);

    Task<DirectoryListing?> ListAsync(string path, bool includeFiles, CancellationToken cancellationToken);

    Task<DirectoryProbe?> ResolveAsync(string path, CancellationToken cancellationToken);
}
