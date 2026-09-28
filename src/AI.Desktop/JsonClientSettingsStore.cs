namespace AI.Desktop;

using System.Text.Json;

/// <summary>Keeps device preferences across the embedded server's changing port.</summary>
internal sealed class JsonClientSettingsStore(DesktopStart start) : IClientSettingsStore
{
    private readonly string _path = Path.Combine(start.DataDirectory, "client-settings.json");

    public string? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var json = File.ReadAllText(_path);
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? json : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(string json)
    {
        if (json.Length > 4096) return;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return;
            Directory.CreateDirectory(start.DataDirectory);
            File.WriteAllText(_path + ".tmp", json);
            File.Move(_path + ".tmp", _path, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // A failed preference write must not interrupt the page.
        }
    }
}
