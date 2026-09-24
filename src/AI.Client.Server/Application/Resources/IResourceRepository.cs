namespace AI.Client.Application.Resources;

using AI.Client.Contracts.Resources;

public interface IResourceRepository
{
    Task<IReadOnlyList<ResourceDefinition>> ListAsync(Guid projectId, CancellationToken cancellationToken);
    Task<ResourceDefinition> GetOrCreateAsync(Guid projectId, ChatResourceRef reference, CancellationToken cancellationToken);
    Task<ResourceDefinition?> RetireAsync(Guid projectId, Guid id, long expectedRevision, CancellationToken cancellationToken);
    Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken);
}
