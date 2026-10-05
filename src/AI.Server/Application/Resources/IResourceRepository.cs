namespace AI.Application.Resources;

using AI.Contracts.Resources;

public interface IResourceRepository
{
    Task<IReadOnlyList<ResourceDefinition>> ListAsync(Guid projectId, CancellationToken cancellationToken);
    Task<ResourceDefinition> GetOrCreateAsync(Guid projectId, ChatResource reference, CancellationToken cancellationToken);
    Task<ResourceDefinition?> RetireAsync(Guid projectId, Guid id, long expectedRevision, CancellationToken cancellationToken);
    Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken);
}
