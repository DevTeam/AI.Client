namespace AI.Mcp.BuiltIn.Grants;

/// <summary>
/// Enforces session directory grants on file system tool arguments: canonicalizes the path,
/// resolves reparse points along every existing component and then checks containment.
/// </summary>
public sealed class PathGuard : IPathGuard
{
    private const int LinkDepthLimit = 40;

    private static readonly StringComparison Comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly DirectoryGrantSpec[] _grants;

    public PathGuard(IGrantSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var grants = new List<DirectoryGrantSpec>();
        foreach (var grant in source.Load())
        {
            if (!Path.IsPathFullyQualified(grant.Root))
            {
                Console.Error.WriteLine($"Ignoring directory grant with a relative root: {grant.Root}");
                continue;
            }

            grants.Add(grant with { Root = Canonicalize(Path.GetFullPath(grant.Root)) });
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

        if (!Path.IsPathFullyQualified(path))
        {
            throw new GrantException($"Path must be absolute: {path}");
        }

        string canonical;
        try
        {
            canonical = Canonicalize(Path.GetFullPath(path));
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

    private static bool Contains(DirectoryGrantSpec grant, string canonical)
    {
        if (string.Equals(grant.Root, canonical, Comparison))
        {
            return true;
        }

        var prefix = grant.Root.EndsWith(Path.DirectorySeparatorChar) ? grant.Root : grant.Root + Path.DirectorySeparatorChar;
        if (!canonical.StartsWith(prefix, Comparison))
        {
            return false;
        }

        return grant.Recursive
               || string.Equals(Path.GetDirectoryName(canonical), grant.Root, Comparison);
    }

    /// <summary>
    /// Walks the path root-down and replaces every existing symlink, junction or other reparse point
    /// with its final target, so a link inside a grant cannot point outside of it.
    /// </summary>
    private static string Canonicalize(string full)
    {
        var current = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(current))
        {
            throw new GrantException($"Path has no root: {full}");
        }

        var segments = full[current.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var links = 0;
        foreach (var segment in segments)
        {
            var next = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            if (info.LinkTarget is null)
            {
                current = next;
                continue;
            }

            if (++links > LinkDepthLimit)
            {
                throw new GrantException($"Too many links while resolving {full}.");
            }

            var target = info.ResolveLinkTarget(returnFinalTarget: true)
                         ?? throw new GrantException($"Broken link while resolving {full}.");
            current = Path.GetFullPath(target.FullName);
        }

        return current;
    }
}
