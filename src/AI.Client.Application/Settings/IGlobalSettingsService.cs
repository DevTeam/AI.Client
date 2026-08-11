using AI.Client.Contracts.Settings;

namespace AI.Client.Application.Settings;

public interface IGlobalSettingsService
{
    Task<GlobalSettings> GetAsync(CancellationToken cancellationToken);

    Task<GlobalSettings> SaveAsync(SaveGlobalSettingsRequest request, CancellationToken cancellationToken);

    Task<bool> SetConnectionCredentialAsync(Guid id, string? value, CancellationToken cancellationToken);

    Task<bool> SetMcpCredentialAsync(Guid id, string? value, CancellationToken cancellationToken);
}
