namespace AI.Web.Resources;

using System.Net.Http.Json;
using System.Text.Json;
using AI.Contracts.Resources;

public sealed class ResourceApi(HttpClient http) : IResourceApi
{
    public async Task<ChatResource> UploadFileAsync(Guid projectId, byte[] bytes, string name,
        ChatResourceSource source, CancellationToken cancellationToken)
    {
        using var body = new ByteArrayContent(bytes);
        using var response = await http.PostAsync($"api/projects/{projectId}/assets?name={Uri.EscapeDataString(name)}&source={source.ToString().ToLowerInvariant()}",
            body, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ChatResource>(cancellationToken)
            ?? throw new InvalidOperationException("The asset service returned no resource.");
    }

    public async Task<string> GetAssetUrlAsync(Guid projectId, string assetId, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"api/projects/{projectId}/assets/{Uri.EscapeDataString(assetId)}/ticket", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return new Uri(http.BaseAddress!, document.RootElement.GetProperty("contentUrl").GetString()).ToString();
    }

    public async Task<ResourceAssetText?> GetAssetTextAsync(Guid projectId, string assetId, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"api/projects/{projectId}/assets/{Uri.EscapeDataString(assetId)}/text", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return response.StatusCode == System.Net.HttpStatusCode.NoContent ? null
            : await response.Content.ReadFromJsonAsync<ResourceAssetText>(cancellationToken);
    }

    public async Task<ChatResource> CreateAsync(Guid projectId, ChatResourceKind kind, string path,
        CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync($"api/projects/{projectId}/resources",
            new CreateResourceRequest(kind, path), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ChatResource>(cancellationToken)
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

    public async Task<ResourceSearchResult> SearchAsync(Guid projectId, string query, int limit,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(
            $"api/projects/{projectId}/resources/search?query={Uri.EscapeDataString(query)}&limit={limit}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ResourceSearchResult>(cancellationToken)
            ?? new ResourceSearchResult([], true);
    }

    public async Task WarmAsync(Guid projectId, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync($"api/projects/{projectId}/resources/index", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkspaceDiffSource>> ListDiffsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"api/projects/{projectId}/resources/diffs", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<WorkspaceDiffSource[]>(cancellationToken) ?? [];
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
