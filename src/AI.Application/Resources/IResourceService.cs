namespace AI.Application.Resources;

using AI.Contracts.Resources;

public interface IResourceService
{
    Task<ChatResource> CreateAsync(Guid projectId, ChatResourceKind kind, string path, CancellationToken cancellationToken);
    Task<IReadOnlyList<ResourceDefinition>> ListAsync(Guid projectId, CancellationToken cancellationToken);
    Task<ResourceDefinition?> RetireAsync(Guid projectId, Guid id, long expectedRevision, CancellationToken cancellationToken);
    Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>The project's readable directories that are in a git work tree and have uncommitted changes.</summary>
    Task<IReadOnlyList<WorkspaceDiffSource>> ListDiffSourcesAsync(Guid projectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChatResource>> ValidateAsync(Guid projectId, IReadOnlyList<ChatResource>? references, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatResource>> ValidateForChatAsync(Guid projectId, Guid chatId,
        IReadOnlyList<ChatResource>? references, CancellationToken cancellationToken);
}
