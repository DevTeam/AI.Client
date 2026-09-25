namespace AI.Client.Application.Resources;

using AI.Client.Contracts.Resources;

/// <summary>
/// Turns paths written in a message into the files and directories they name, so the chat can
/// link them however the model spelled them: absolute, relative to a granted directory, with a
/// line suffix. A relative path is looked for only inside what the project may read; an absolute
/// one outside it is reported as existing but not readable, so the person can grant access.
/// </summary>
public interface IWorkspacePathResolver
{
    /// <summary>One answer per distinct input, in input order; an unresolved path has no Path and no Kind.</summary>
    Task<IReadOnlyList<ResolvedPath>> ResolveAsync(Guid projectId, IReadOnlyList<string> paths,
        CancellationToken cancellationToken);
}
