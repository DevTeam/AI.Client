namespace AI.Desktop;

internal interface IClientSettingsStore
{
    string? Load();

    void Save(string json);
}
