namespace AI.Infrastructure.Settings;

public interface IGlobalSettingsPaths
{
    string SettingsPath { get; }
    string GetSecretPath(string scope, Guid id);
}
