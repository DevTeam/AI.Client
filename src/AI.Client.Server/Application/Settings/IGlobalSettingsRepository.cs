namespace AI.Client.Application.Settings;

using AI.Client.Contracts.Settings;

public interface IGlobalSettingsRepository
{
    Task<GlobalSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(GlobalSettings settings, CancellationToken cancellationToken);
}
