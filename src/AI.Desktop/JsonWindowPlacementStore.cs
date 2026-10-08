namespace AI.Desktop;

using System.Text.Json;
using AI.Contracts.FileSystem;

/// <summary>Keeps the placement in the data directory, next to the web view's profile.</summary>
internal sealed class JsonWindowPlacementStore(DesktopStart start, IFileSystem files) : IWindowPlacementStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path = Path.Combine(start.DataDirectory, "window.json");

    public WindowPlacement? Load()
    {
        try
        {
            var json = SynchronousFiles.Complete(() => files.ReadTextAsync(_path, CancellationToken.None));
            return json is null ? null : JsonSerializer.Deserialize<WindowPlacement>(json, Options);
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
            var json = JsonSerializer.Serialize(placement, Options);
            SynchronousFiles.Complete(() => files.WriteTextAsync(_path + ".tmp", json, CancellationToken.None));
            SynchronousFiles.Complete(() => files.MoveAsync(_path + ".tmp", _path, true, CancellationToken.None));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember the placement must not stop the app from closing.
        }
    }
}
