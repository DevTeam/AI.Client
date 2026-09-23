namespace AI.Client.Infrastructure.Settings;

public sealed class GlobalSettingsPaths(string rootDirectory) : IGlobalSettingsPaths
{
    public string SettingsPath { get; } = Path.Combine(rootDirectory, "settings.json");

    public string GetSecretPath(string scope, Guid id) =>
        Path.Combine(rootDirectory, "credentials", $"{scope}-{id:N}.protected");
}
