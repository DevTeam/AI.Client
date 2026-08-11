using AI.Client.Contracts.Settings;

namespace AI.Client.Application.Settings;

public interface IGlobalSettingsRepository
{
    Task<GlobalSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(GlobalSettings settings, CancellationToken cancellationToken);
}
