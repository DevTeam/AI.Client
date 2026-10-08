namespace AI.Mcp.BuiltIn.Files;

using AI.Contracts.FileSystem;

/// <summary>
/// Tells a link from an ordinary entry through the file system contract alone, so the directory
/// walkers never ask <c>System.IO</c> and a fake answers the question in a test.
/// </summary>
internal static class LinkTargets
{
    /// <summary>
    /// Whether <paramref name="path"/> is a symlink or junction, which the walkers list but never
    /// descend through: following one leads back up the tree, and a walk that leaves the granted
    /// root reports what the caller was not given access to.
    /// </summary>
    /// <remarks>
    /// The <see cref="FileAttributes.ReparsePoint"/> bit is only a cheap pre-filter, because
    /// <c>FileSystemInfo.LinkTarget</c> — the test this code used before the contract existed — is
    /// narrower than the attribute: a cloud-sync placeholder carries the bit with nothing on the
    /// other end of it, and a placeholder is a directory like any other. So the entry is resolved
    /// and compared with its own canonical path: a target that differs means a link was followed
    /// and the entry is skipped, while a target that comes back the same means there is no link and
    /// the entry is walked into as before.
    /// </remarks>
    internal static async Task<bool> IsLinkAsync(
        IFileSystem files,
        IPath paths,
        string path,
        FileAttributes attributes,
        CancellationToken cancellationToken)
    {
        if ((attributes & FileAttributes.ReparsePoint) == 0)
        {
            return false;
        }

        var canonical = paths.GetFullPath(path);
        var resolved = await files.ResolveLinkTargetAsync(canonical, cancellationToken);
        return !string.Equals(resolved, canonical, paths.Comparison);
    }
}
