namespace AI.Desktop;

using System.Text.Json;
using AI.Contracts.FileSystem;

internal sealed class JsonThemePreferenceStore(DesktopStart start, IFileSystem files) : IThemePreferenceStore
{
    private readonly string _path = Path.Combine(start.DataDirectory, "theme.json");

    public string Load()
    {
        try
        {
            var json = SynchronousFiles.Complete(() => files.ReadTextAsync(_path, CancellationToken.None));
            return json is null ? "system" : Normalize(JsonSerializer.Deserialize<ThemePreference>(json)?.Preference);
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
            var json = JsonSerializer.Serialize(new ThemePreference(Normalize(preference)));
            SynchronousFiles.Complete(() => files.WriteTextAsync(_path + ".tmp", json, CancellationToken.None));
            SynchronousFiles.Complete(() => files.MoveAsync(_path + ".tmp", _path, true, CancellationToken.None));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The window then starts in the system theme next time and catches up once the page loads.
        }
    }

    private static string Normalize(string? preference) =>
        preference is "light" or "dark" or "darkblue" or "gray" or "lightgray" ? preference : "system";

    private sealed record ThemePreference(string Preference);
}
