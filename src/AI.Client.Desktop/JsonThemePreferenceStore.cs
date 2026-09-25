namespace AI.Client.Desktop;

using System.Text.Json;

internal sealed class JsonThemePreferenceStore(DesktopStart start) : IThemePreferenceStore
{
    private readonly string _path = Path.Combine(start.DataDirectory, "theme.json");

    public string Load()
    {
        try
        {
            if (!File.Exists(_path)) return "system";
            return Normalize(JsonSerializer.Deserialize<ThemePreference>(File.ReadAllText(_path))?.Preference);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return "system";
        }
    }

    public void Save(string preference)
    {
        try
        {
            Directory.CreateDirectory(start.DataDirectory);
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(new ThemePreference(Normalize(preference))));
            File.Move(_path + ".tmp", _path, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The titlebar then starts in the system theme next time and catches up once the page loads.
        }
    }

    private static string Normalize(string? preference) => preference is "light" or "dark" ? preference : "system";

    private sealed record ThemePreference(string Preference);
}
