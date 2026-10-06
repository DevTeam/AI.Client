namespace AI.Application.Resources;

using AI.Contracts.Resources;

public sealed record ResourceAsset(byte[] Data, string MediaType);
public sealed record ResourceTicket(string ContentUrl);

public interface IResourceAssetService
{
    Task<ChatResource> StoreAsync(Guid projectId, byte[] data, string name, ChatResourceSource source,
        string sourceLocation, CancellationToken cancellationToken);
    Task<ResourceAsset?> ReadAsync(Guid projectId, string assetId, CancellationToken cancellationToken);
    Task<ResourceAssetText?> ReadTextAsync(Guid projectId, string assetId, CancellationToken cancellationToken);
    Task<ResourceTicket?> CreateTicketAsync(Guid projectId, string assetId, CancellationToken cancellationToken);
    Task<ResourceAsset?> ReadTicketAsync(string ticket, CancellationToken cancellationToken);
    /// <summary>Stores bytes for workspace Undo without creating a chat resource or a public content ticket.</summary>
    Task<string> StoreUndoBytesAsync(Guid projectId, byte[] data, CancellationToken cancellationToken);
    Task<byte[]?> ReadUndoBytesAsync(Guid projectId, string assetId, CancellationToken cancellationToken);
    Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken);
}
