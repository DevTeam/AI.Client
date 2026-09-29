namespace AI.Web.Resources;

using AI.Contracts.Resources;

public interface IResourceApi
{
    Task<ChatResourceRef> CreateAsync(Guid projectId, ChatResourceKind kind, string path, CancellationToken cancellationToken);

    /// <summary>What each path names and whether the project may read it; see <see cref="ResolvedPath"/>.</summary>
    Task<IReadOnlyList<ResolvedPath>> ResolveAsync(Guid projectId, IReadOnlyList<string> paths, CancellationToken cancellationToken);

    /// <summary>Files and directories of the project whose name or relative path matches <paramref name="query"/>.</summary>
    Task<IReadOnlyList<ResourceSuggestion>> SearchAsync(Guid projectId, string query, int limit, CancellationToken cancellationToken);

    /// <summary>The project's directories with uncommitted git changes.</summary>
    Task<IReadOnlyList<WorkspaceDiffSource>> ListDiffsAsync(Guid projectId, CancellationToken cancellationToken);
}
