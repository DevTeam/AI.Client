namespace AI.Contracts.FileSystem;

/// <summary>
/// One file or directory as the file system reports it.
/// </summary>
/// <param name="Path">
/// The path that was asked for, not the path the entry turns out to live at: a link is reported
/// under the link itself, while its metadata come from the target.
/// </param>
/// <param name="Name">The last segment of <paramref name="Path"/>, as people call the entry.</param>
/// <param name="IsDirectory">
/// Whether the entry is a directory. For a link this is the kind of the target, not of the link.
/// </param>
/// <param name="Length">Size in bytes; 0 for a directory, which has no length of its own.</param>
/// <param name="Attributes">
/// The platform's attributes for the entry, so a caller filtering entries can do it from what it
/// was given instead of asking again. A fake reports what it models; it is <see cref="FileAttributes.Normal"/>
/// by default because that is what an ordinary entry is.
/// </param>
public sealed record FileSystemEntry(
    string Path,
    string Name,
    bool IsDirectory,
    long Length,
    FileAttributes Attributes = FileAttributes.Normal);
