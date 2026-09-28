using AI.Infrastructure.Storage;

namespace AI.Infrastructure.Settings;

public sealed class GlobalSettingsPaths(IProjectStorageLocation location) : IGlobalSettingsPaths
{
    public string SettingsPath { get; } = Path.Combine(location.RootDirectory, "settings.json");

    public string GetSecretPath(string scope, Guid id) =>
        Path.Combine(location.RootDirectory, "credentials", $"{scope}-{id:N}.protected");
}
