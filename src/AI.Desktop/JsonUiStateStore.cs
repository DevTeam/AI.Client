namespace AI.Desktop;

using System.Text.Json;
using AI.Contracts.FileSystem;

/// <summary>Keeps the page's remembered state in <c>ui-state.json</c> across the server's changing port.</summary>
internal sealed class JsonUiStateStore(DesktopStart start, IFileSystem files) : IUiStateStore
{
    // Drafts and composer history are the largest entries; anything near this is not UI state.
    private const int MaxLength = 4 * 1024 * 1024;

    private readonly string _path = Path.Combine(start.DataDirectory, "ui-state.json");

    public string? Load()
    {
        try
        {
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
        if (json.Length > MaxLength) return;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return;
            SynchronousFiles.Complete(() => files.WriteTextAsync(_path + ".tmp", json, CancellationToken.None));
            SynchronousFiles.Complete(() => files.MoveAsync(_path + ".tmp", _path, true, CancellationToken.None));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // A failed write only costs what the next start remembers.
        }
    }
}
