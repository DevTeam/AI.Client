namespace AI.Desktop;

using System.Text.Json;

/// <summary>Persists the desktop workspace independently of the server's changing port.</summary>
internal sealed class JsonWorkspaceLocationStore(DesktopStart start) : IWorkspaceLocationStore
{
    private readonly string _path = Path.Combine(start.DataDirectory, "workspace-location.json");

    public Uri? Restore(Uri address)
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var location = JsonSerializer.Deserialize<WorkspaceLocation>(File.ReadAllText(_path));
            if (location?.ProjectId is not { } projectId) return null;
            var query = $"project={projectId:D}";
            if (location.ChatId is { } chatId) query += $"&chat={chatId:D}";
            if (location.BranchId is { } branchId && location.ChatId is not null)
                query += $"&branch={branchId:D}";
            return new UriBuilder(address) { Query = query }.Uri;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(Guid? projectId, Guid? chatId, Guid? branchId)
    {
        try
        {
            Directory.CreateDirectory(start.DataDirectory);
            var location = new WorkspaceLocation(projectId, projectId is null ? null : chatId,
                chatId is null ? null : branchId);
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(location));
            File.Move(_path + ".tmp", _path, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Failure to remember a UI location must not interrupt navigation.
        }
    }

    private sealed record WorkspaceLocation(Guid? ProjectId, Guid? ChatId, Guid? BranchId);
}
