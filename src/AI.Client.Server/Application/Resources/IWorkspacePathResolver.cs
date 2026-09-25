namespace AI.Client.Application.Resources;

using AI.Client.Contracts.Resources;

/// <summary>
/// Turns paths written in a message into the files and directories they name, so the chat can
/// link them however the model spelled them: absolute, relative to a granted directory, with a
/// line suffix. Only what the project may read is ever reported.
/// </summary>
public interface IWorkspacePathResolver
{
    /// <summary>One answer per distinct input, in input order; an unresolved path has no Path and no Kind.</summary>
    Task<IReadOnlyList<ResolvedPath>> ResolveAsync(Guid projectId, IReadOnlyList<string> paths,
        CancellationToken cancellationToken);
}
