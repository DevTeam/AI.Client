namespace AI.Client.Contracts.FileSystem;

/// <summary>
/// The contents of one level of the host's file system.
/// </summary>
/// <param name="CurrentPath">
/// The directory that was listed. Empty for the roots level (drives on Windows, "/" elsewhere),
/// which is the level above every path.
/// </param>
/// <param name="ParentPath">
/// Where "up" leads: a path, or the empty string for the roots level, or null when there is no
/// level above this one because this <em>is</em> the roots level.
/// </param>
/// <param name="IsAccessible">
/// False when the directory is there but will not open for the account running the host. The
/// listing is then empty for a reason worth saying out loud, rather than looking like an empty
/// folder.
/// </param>
public sealed record DirectoryListing(
    string CurrentPath,
    string? ParentPath,
    bool IsAccessible,
    IReadOnlyList<DirectoryEntry> Directories);
