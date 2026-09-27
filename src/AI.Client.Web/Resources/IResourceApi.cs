namespace AI.Client.Web.Resources;

using AI.Client.Contracts.Resources;

public interface IResourceApi
{
    Task<ChatResourceRef> CreateAsync(Guid projectId, ChatResourceKind kind, string path, CancellationToken cancellationToken);

    /// <summary>What each path names and whether the project may read it; see <see cref="ResolvedPath"/>.</summary>
    Task<IReadOnlyList<ResolvedPath>> ResolveAsync(Guid projectId, IReadOnlyList<string> paths, CancellationToken cancellationToken);
}
