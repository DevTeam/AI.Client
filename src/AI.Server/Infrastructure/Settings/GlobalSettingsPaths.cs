using AI.Infrastructure.Storage;

namespace AI.Infrastructure.Settings;

public sealed class GlobalSettingsPaths(IUserStorageLocation location) : IGlobalSettingsPaths
{
    public string SettingsPath => Path.Combine(location.RootDirectory, "settings.json");

    public string GetSecretPath(string scope, Guid id) =>
        Path.Combine(location.RootDirectory, "credentials", $"{scope}-{id:N}.protected");
}
