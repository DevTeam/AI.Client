namespace AI.Application.Resources;

using AI.Contracts.Projects;
using AI.Contracts.Resources;

public sealed class ProjectPathAccess : IProjectPathAccess
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public string ResolveLinks(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? throw new ArgumentException("Path has no root.");
        var current = root;
        var depth = 0;
        foreach (var segment in full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            if (info.LinkTarget is null) { current = next; continue; }
            if (++depth > 40) throw new ArgumentException("Too many links in resource path.");
            current = info.ResolveLinkTarget(true)?.FullName
                ?? throw new ArgumentException("Resource path contains a broken link.");
        }
        return current;
    }

    public bool CanRead(ProjectDetails project, string path) => AccessOf(project, path) != PathAccess.None;

    public PathAccess AccessOf(ProjectDetails project, string path)
    {
        ArgumentNullException.ThrowIfNull(project);
        var access = PathAccess.None;
        foreach (var grant in project.DirectoryGrants)
        {
            if (!grant.ToolNames.Contains("read", StringComparer.OrdinalIgnoreCase)
                || !InGrant(path, ResolveLinks(grant.CanonicalRoot), grant.Recursive)) continue;
            if (grant.ToolNames.Contains("write", StringComparer.OrdinalIgnoreCase)) return PathAccess.ReadWrite;
            access = PathAccess.Read;
        }
        return access;
    }

    private static bool InGrant(string path, string root, bool recursive)
    {
        if (string.Equals(path, root, PathComparison)) return true;
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, PathComparison)
            && (recursive || string.Equals(Path.GetDirectoryName(path), root, PathComparison));
    }
}
