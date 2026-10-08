namespace AI.Desktop;

using System.Text.Json;
using AI.Contracts.FileSystem;

/// <summary>Persists the desktop workspace independently of the server's changing port.</summary>
internal sealed class JsonWorkspaceLocationStore(DesktopStart start, IFileSystem files) : IWorkspaceLocationStore
{
    private readonly string _path = Path.Combine(start.DataDirectory, "workspace-location.json");

    public Uri? Restore(Uri address)
    {
        try
        {
            var json = SynchronousFiles.Complete(() => files.ReadTextAsync(_path, CancellationToken.None));
            if (json is null) return null;
            var location = JsonSerializer.Deserialize<WorkspaceLocation>(json);
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
            var location = new WorkspaceLocation(projectId, projectId is null ? null : chatId,
                chatId is null ? null : branchId);
            var json = JsonSerializer.Serialize(location);
            SynchronousFiles.Complete(() => files.WriteTextAsync(_path + ".tmp", json, CancellationToken.None));
            SynchronousFiles.Complete(() => files.MoveAsync(_path + ".tmp", _path, true, CancellationToken.None));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Failure to remember a UI location must not interrupt navigation.
        }
    }

    private sealed record WorkspaceLocation(Guid? ProjectId, Guid? ChatId, Guid? BranchId);
}
