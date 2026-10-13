namespace AI.Application.Resources;

using AI.Application.Tools;
using AI.Contracts.FileSystem;
using AI.Contracts.Projects;
using AI.Contracts.Resources;

public sealed class ProjectPathAccess(IFileSystem files, IPath paths, IChatTemporaryDirectory temporary)
    : IProjectPathAccess
{
    /// <summary>
    /// Resolves the path the way the contract's link resolver does: the canonical path with every
    /// symbolic link on the way already followed. It is the only link-resolution member the frozen
    /// contract exposes, so a path whose resolved form differs from its own spelling is one that
    /// leads somewhere else, and the containment checks below then judge where it really leads.
    /// The enclosing members are synchronous and the resolver completes synchronously on both the
    /// adapter and the fake, so the completed task is bridged rather than the signature widened.
    /// </summary>
    public string ResolveLinks(string path) =>
        files.ResolveLinkTargetAsync(path, CancellationToken.None).GetAwaiter().GetResult();

    public bool CanRead(ProjectDetails project, string path) => AccessOf(project, path) != PathAccess.None;

    public PathAccess AccessOf(ProjectDetails project, string path)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (IsTemporary(project, path)) return PathAccess.ReadWrite;
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

    /// <summary>
    /// Whether the path lies in the project's chat scratch space, which every run of the project is
    /// granted read and write to without a stored grant. Links above the scratch space (the user's
    /// own temporary directory) are followed, but the two levels the application creates count only
    /// while neither is a link, so one replaced by a junction never opens the place it points at.
    /// </summary>
    public bool IsTemporary(ProjectDetails project, string path)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.Id == Guid.Empty) return false;
        var root = paths.TrimEndingDirectorySeparator(paths.GetFullPath(temporary.ProjectRoot(project.Id)));
        if (paths.GetDirectoryName(root) is not { } userRoot || paths.GetDirectoryName(userRoot) is not { } systemRoot)
            return false;
        var expected = paths.Combine(paths.Combine(ResolveLinks(systemRoot), paths.GetFileName(userRoot)),
            paths.GetFileName(root));
        var resolved = paths.TrimEndingDirectorySeparator(ResolveLinks(root));
        return string.Equals(resolved, paths.TrimEndingDirectorySeparator(expected), paths.Comparison)
               && InGrant(path, resolved, recursive: true);
    }

    private bool InGrant(string path, string root, bool recursive) => paths.IsInside(path, root, recursive);
}
