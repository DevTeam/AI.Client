namespace AI.Client.Web.Settings;

using AI.Client.Contracts.Settings;

public interface IGlobalSettingsApi
{
    Task<IReadOnlyList<McpToolInfo>> GetDefaultToolsAsync(CancellationToken cancellationToken);
    Task<GlobalSettings> GetAsync(CancellationToken cancellationToken);

    Task<GlobalSettings> SaveAsync(SaveGlobalSettingsRequest request, CancellationToken cancellationToken);

    Task SetConnectionCredentialAsync(Guid id, string? value, CancellationToken cancellationToken);

    Task SetMcpCredentialAsync(Guid id, string? value, CancellationToken cancellationToken);
}
