namespace AI.Client.Application.Resources;

using AI.Client.Contracts.Resources;

public interface IResourceService
{
    Task<ChatResourceRef> CreateAsync(Guid projectId, ChatResourceKind kind, string path, CancellationToken cancellationToken);
    Task<IReadOnlyList<ResourceDefinition>> ListAsync(Guid projectId, CancellationToken cancellationToken);
    Task<ResourceDefinition?> RetireAsync(Guid projectId, Guid id, long expectedRevision, CancellationToken cancellationToken);
    Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatResourceRef>> ValidateAsync(Guid projectId, IReadOnlyList<ChatResourceRef>? references, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatResourceRef>> ValidateForChatAsync(Guid projectId, Guid chatId,
        IReadOnlyList<ChatResourceRef>? references, CancellationToken cancellationToken);
}
