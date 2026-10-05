namespace AI.Web.Resources;

using AI.Contracts.Resources;

public interface IResourceApi
{
    Task<ChatResource> CreateAsync(Guid projectId, ChatResourceKind kind, string path, CancellationToken cancellationToken);
    Task<ChatResource> UploadFileAsync(Guid projectId, byte[] bytes, string name, ChatResourceSource source,
        CancellationToken cancellationToken);
    Task<string> GetAssetUrlAsync(Guid projectId, string assetId, CancellationToken cancellationToken);
    Task<ResourceAssetText?> GetAssetTextAsync(Guid projectId, string assetId, CancellationToken cancellationToken);

    /// <summary>What each path names and whether the project may read it; see <see cref="ResolvedPath"/>.</summary>
    Task<IReadOnlyList<ResolvedPath>> ResolveAsync(Guid projectId, IReadOnlyList<string> paths, CancellationToken cancellationToken);

    /// <summary>Files and directories of the project whose name or relative path matches <paramref name="query"/>.</summary>
    Task<ResourceSearchResult> SearchAsync(Guid projectId, string query, int limit, CancellationToken cancellationToken);

    /// <summary>Asks the Host to index the project's directories now, before the first "@".</summary>
    Task WarmAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>The project's directories with uncommitted git changes.</summary>
    Task<IReadOnlyList<WorkspaceDiffSource>> ListDiffsAsync(Guid projectId, CancellationToken cancellationToken);
}
