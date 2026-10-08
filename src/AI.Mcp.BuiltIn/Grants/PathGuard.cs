namespace AI.Mcp.BuiltIn.Grants;

using AI.Contracts.FileSystem;

/// <summary>
/// Enforces session directory grants on file system tool arguments: canonicalizes the path,
/// resolves reparse points along every existing component and then checks containment.
/// </summary>
public sealed class PathGuard : IPathGuard
{
    private const int LinkDepthLimit = 40;

    private readonly DirectoryGrantSpec[] _grants;
    private readonly IPath _path;
    private readonly IFileSystem _files;

    /// <param name="path">
    /// The path semantics this session judges with. Optional so that a caller which already knows it
    /// wants the platform does not have to say so; the composition injects the bound implementations.
    /// </param>
    /// <param name="files">The file system the link walk asks about links.</param>
    public PathGuard(IGrantSource source, IPath? path = null, IFileSystem? files = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        _path = path ?? new SystemPath();
        _files = files ?? new SystemFileSystem();
        var grants = new List<DirectoryGrantSpec>();
        foreach (var grant in source.Load())
        {
            if (!_path.IsFullyQualified(grant.Root))
            {
                Console.Error.WriteLine($"Ignoring directory grant with a relative root: {grant.Root}");
                continue;
            }

            grants.Add(grant with { Root = Canonicalize(_path.GetFullPath(grant.Root)) });
        }

        _grants = grants.ToArray();
    }

    public IReadOnlyList<DirectoryGrantSpec> Grants => _grants;

    public string Resolve(string path, GrantCapability capability)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new GrantException("Path cannot be empty.");
        }

        if (path.Length > 4096)
        {
            throw new GrantException("Path exceeds the length limit.");
        }

        if (!_path.IsFullyQualified(path))
        {
            throw new GrantException($"Path must be absolute: {path}");
        }

        string canonical;
        try
        {
            canonical = Canonicalize(_path.GetFullPath(path));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            throw new GrantException($"Path cannot be resolved: {path}", error);
        }

        foreach (var grant in _grants)
        {
            if (grant.Capabilities.Contains(capability) && Contains(grant, canonical))
            {
                return canonical;
            }
        }

        var name = capability.ToString().ToLowerInvariant();
        throw new GrantException(_grants.Length == 0
            ? $"No directory grant is configured for this session, so '{name}' access is denied. Use list_allowed_directories."
            : $"No directory grant allows '{name}' access to {canonical}. Use list_allowed_directories.");
    }

    /// <summary>
    /// Containment is a segment-wise walk over the contract rather than a hand-built string prefix:
    /// the root is inside itself, a child has to follow a separator and may not be <c>..</c>, and a
    /// grandchild needs the grant to be recursive. Comparison follows the platform, so on Windows
    /// the check ignores case and elsewhere it does not — <c>ab</c> is never inside <c>a</c>.
    /// </summary>
    private bool Contains(DirectoryGrantSpec grant, string canonical) =>
        _path.IsInside(canonical, grant.Root, grant.Recursive);

    /// <summary>
    /// Walks the path root-down and replaces every existing symlink, junction or other reparse point
    /// with its final target, so a link inside a grant cannot point outside of it. A segment that is
    /// not there ends the walk where it would have been created, which is what lets a path naming
    /// nothing still be judged to be inside or outside a grant.
    /// </summary>
    private string Canonicalize(string full)
    {
        var current = _path.GetPathRoot(full);
        if (string.IsNullOrEmpty(current))
        {
            throw new GrantException($"Path has no root: {full}");
        }

        var segments = full[current.Length..]
            .Split([_path.DirectorySeparator, _path.AltDirectorySeparator], StringSplitOptions.RemoveEmptyEntries);
        var links = 0;
        foreach (var segment in segments)
        {
            var next = _path.Combine(current, segment);
            var resolved = Resolve(next);
            // The member answers with the canonical path of whatever the segment resolves to. When
            // that is not the segment itself, a link was followed and it counts against the limit.
            if (!string.Equals(resolved, next, _path.Comparison) && ++links > LinkDepthLimit)
            {
                throw new GrantException($"Too many links while resolving {full}.");
            }

            current = resolved;
        }

        return current;
    }

    /// <summary>
    /// The path with every link followed. A containment check is synchronous and this answer is
    /// computed without waiting for anything, so the completed task's result is read directly: the
    /// contract records that decision for exactly this caller, and a stdio server has no
    /// synchronization context to deadlock on.
    /// </summary>
    private string Resolve(string path) =>
        _files.ResolveLinkTargetAsync(path, CancellationToken.None).GetAwaiter().GetResult();
}
