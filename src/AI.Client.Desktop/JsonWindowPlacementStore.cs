namespace AI.Client.Desktop;

using System.Text.Json;

/// <summary>Keeps the placement in the data directory, next to the web view's profile.</summary>
internal sealed class JsonWindowPlacementStore(DesktopStart start) : IWindowPlacementStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path = Path.Combine(start.DataDirectory, "window.json");

    public WindowPlacement? Load()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(_path), Options) : null;
        }
        // A damaged or unreadable file only costs the placement; the window opens at its default.
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(WindowPlacement placement)
    {
        try
        {
            Directory.CreateDirectory(start.DataDirectory);
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(placement, Options));
            File.Move(_path + ".tmp", _path, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember the placement must not stop the app from closing.
        }
    }
}
