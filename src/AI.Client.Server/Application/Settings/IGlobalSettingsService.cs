namespace AI.Client.Application.Settings;

using AI.Client.Contracts.Settings;

public interface IGlobalSettingsService
{
    Task<GlobalSettings> GetAsync(CancellationToken cancellationToken);

    Task<GlobalSettings> SaveAsync(SaveGlobalSettingsRequest request, CancellationToken cancellationToken);

    Task<bool> SetConnectionCredentialAsync(Guid id, string? value, CancellationToken cancellationToken);

    Task<bool> SetMcpCredentialAsync(Guid id, string? value, CancellationToken cancellationToken);
    Task<GlobalSettings> SetToolPolicyAsync(McpToolPolicySettings policy, CancellationToken cancellationToken);
    Task<GlobalSettings> RemoveToolPolicyAsync(Guid serverId, string name, string schemaHash, CancellationToken cancellationToken);
}
