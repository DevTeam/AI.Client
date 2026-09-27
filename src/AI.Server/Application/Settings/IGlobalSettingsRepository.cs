namespace AI.Application.Settings;

using AI.Contracts.Settings;

public interface IGlobalSettingsRepository
{
    Task<GlobalSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(GlobalSettings settings, CancellationToken cancellationToken);
}
