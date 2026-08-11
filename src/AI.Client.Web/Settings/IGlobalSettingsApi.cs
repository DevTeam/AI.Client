using AI.Client.Contracts.Settings;

namespace AI.Client.Web.Settings;

public interface IGlobalSettingsApi
{
    Task<GlobalSettings> GetAsync(CancellationToken cancellationToken);

    Task<GlobalSettings> SaveAsync(SaveGlobalSettingsRequest request, CancellationToken cancellationToken);

    Task SetConnectionCredentialAsync(Guid id, string? value, CancellationToken cancellationToken);

    Task SetMcpCredentialAsync(Guid id, string? value, CancellationToken cancellationToken);
}
