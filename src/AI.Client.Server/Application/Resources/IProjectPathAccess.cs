namespace AI.Client.Application.Resources;

using AI.Client.Contracts.Projects;

/// <summary>
/// Whether a project may read a path. Links are resolved first, so a link inside a granted
/// directory cannot lead outside it. Resources and path resolution share this so they cannot
/// disagree about what a project can see.
/// </summary>
public interface IProjectPathAccess
{
    /// <summary>The path with every symbolic link on the way resolved; throws <see cref="ArgumentException"/> for a broken one.</summary>
    string ResolveLinks(string path);

    /// <summary>True when a grant with the read tool covers the already link-resolved <paramref name="path"/>.</summary>
    bool CanRead(ProjectDetails project, string path);
}
