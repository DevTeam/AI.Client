namespace AI.Client.Web.FileSystem;

using AI.Client.Contracts.FileSystem;
using System.Net;
using System.Net.Http.Json;

public sealed class FileSystemApi(HttpClient httpClient) : IFileSystemApi
{
    public Task<DirectoryListing?> ListRootsAsync(CancellationToken cancellationToken) =>
        GetAsync<DirectoryListing>("api/filesystem/roots", cancellationToken);

    public Task<DirectoryListing?> ListAsync(string path, bool includeFiles, CancellationToken cancellationToken) =>
        GetAsync<DirectoryListing>(
            $"api/filesystem/directories?path={Uri.EscapeDataString(path)}&includeFiles={(includeFiles ? "true" : "false")}",
            cancellationToken);

    public Task<DirectoryProbe?> ResolveAsync(string path, CancellationToken cancellationToken) =>
        GetAsync<DirectoryProbe>($"api/filesystem/resolve?path={Uri.EscapeDataString(path)}", cancellationToken);

    private async Task<T?> GetAsync<T>(string requestUri, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }
}
