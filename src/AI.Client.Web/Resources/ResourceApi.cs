namespace AI.Client.Web.Resources;

using System.Net.Http.Json;
using System.Text.Json;
using AI.Client.Contracts.Resources;

public sealed class ResourceApi(HttpClient http) : IResourceApi
{
    public async Task<ChatResourceRef> CreateAsync(Guid projectId, ChatResourceKind kind, string path,
        CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync($"api/projects/{projectId}/resources",
            new CreateResourceRequest(kind, path), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ChatResourceRef>(cancellationToken)
            ?? throw new InvalidOperationException("The resource service returned no reference.");
    }

    public async Task<IReadOnlyList<ResolvedPath>> ResolveAsync(Guid projectId, IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync($"api/projects/{projectId}/resources/resolve",
            new ResolvePathsRequest(paths), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ResolvedPath[]>(cancellationToken) ?? [];
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body);
            var detail = document.RootElement.TryGetProperty("detail", out var value) ? value.GetString() : null;
            throw new InvalidOperationException(detail ?? $"Resource request failed ({(int)response.StatusCode}).");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"Resource request failed ({(int)response.StatusCode}).");
        }
    }
}
