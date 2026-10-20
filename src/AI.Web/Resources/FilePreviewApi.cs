namespace AI.Web.Resources;

using System.Net;
using System.Net.Http.Json;
using AI.Contracts.Resources;

public sealed class FilePreviewApi(HttpClient http) : IFilePreviewApi
{
    public Task<FilePreview> DescribeAsync(Guid projectId, string path, CancellationToken cancellationToken) =>
        GetAsync<FilePreview>(projectId, path, "preview", 0, cancellationToken);

    public Task<FilePreviewText> ReadTextAsync(Guid projectId, string path, int offset, CancellationToken cancellationToken) =>
        GetAsync<FilePreviewText>(projectId, path, "preview-text", offset, cancellationToken);

    public async Task OpenAsync(Guid projectId, string path, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync($"api/projects/{projectId}/resources/open?path={Uri.EscapeDataString(path)}", null, cancellationToken);
        EnsureSuccess(response, "open");
    }

    private async Task<T> GetAsync<T>(Guid projectId, string path, string action, int offset, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"api/projects/{projectId}/resources/{action}?path={Uri.EscapeDataString(path)}&offset={offset}", cancellationToken);
        EnsureSuccess(response, "load");
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new InvalidOperationException("The Host returned no file information.");
    }

    private static void EnsureSuccess(HttpResponseMessage response, string verb)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(response.StatusCode switch
            {
                HttpStatusCode.Forbidden => "The project does not have read access to this file.",
                HttpStatusCode.NotFound => "The file or directory is no longer available.",
                HttpStatusCode.Conflict => "The file is unavailable or busy. Try again.",
                HttpStatusCode.UnprocessableEntity => "The archive is invalid or unsupported.",
                _ => $"Could not {verb} the file ({(int)response.StatusCode})."
            });
    }
}
