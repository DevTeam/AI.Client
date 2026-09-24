namespace AI.Client.Web.Resources;

using AI.Client.Contracts.Resources;

public interface IResourceApi
{
    Task<ChatResourceRef> CreateAsync(Guid projectId, ChatResourceKind kind, string path, CancellationToken cancellationToken);
}
