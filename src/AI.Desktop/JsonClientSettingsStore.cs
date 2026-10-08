namespace AI.Desktop;

using System.Text.Json;
using AI.Contracts.FileSystem;

/// <summary>Keeps device preferences across the embedded server's changing port.</summary>
internal sealed class JsonClientSettingsStore(DesktopStart start, IFileSystem files) : IClientSettingsStore
{
    private readonly string _path = Path.Combine(start.DataDirectory, "client-settings.json");

    public string? Load()
    {
        try
        {
            // A document that is not there reads as absent; the writer creates the directory.
            var json = SynchronousFiles.Complete(() => files.ReadTextAsync(_path, CancellationToken.None));
            if (json is null) return null;
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
            SynchronousFiles.Complete(() => files.WriteTextAsync(_path + ".tmp", json, CancellationToken.None));
            SynchronousFiles.Complete(() => files.MoveAsync(_path + ".tmp", _path, true, CancellationToken.None));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // A failed preference write must not interrupt the page.
        }
    }
}
