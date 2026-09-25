namespace AI.Client.Web.Memory;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.Client.Contracts.Instructions;
using AI.Client.Contracts.Memory;

public sealed class MemoryApi(HttpClient http) : IMemoryApi
{
    public async Task<IReadOnlyList<MemoryEntry>> ListAsync(Guid? projectId, CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<IReadOnlyList<MemoryEntry>>(
            projectId is { } id ? $"api/memory?projectId={id}" : "api/memory", cancellationToken) ?? [];

    public async Task<MemoryEntry> CreateAsync(CreateMemoryEntryRequest request, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("api/memory", request, cancellationToken);
        return await ReadAsync<MemoryEntry>(response, cancellationToken);
    }

    public async Task<MemoryEntry> UpdateAsync(MemoryEntry entry, UpdateMemoryEntryRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await http.PutAsJsonAsync($"api/memory/{entry.Id}{Owner(entry)}", request, cancellationToken);
        return await ReadAsync<MemoryEntry>(response, cancellationToken);
    }

    public async Task DeleteAsync(MemoryEntry entry, CancellationToken cancellationToken)
    {
        var owner = Owner(entry);
        using var response = await http.DeleteAsync(
            $"api/memory/{entry.Id}{(owner.Length == 0 ? "?" : owner + "&")}revision={entry.Revision}", cancellationToken);
        await EnsureAsync(response, cancellationToken);
    }

    public async Task<ProjectInstructions> GetInstructionsAsync(Guid projectId, CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<ProjectInstructions>($"api/projects/{projectId}/instructions", cancellationToken)
        ?? throw new InvalidOperationException("The project has no instructions document.");

    public async Task<ProjectInstructions> UpdateInstructionsAsync(Guid projectId, UpdateProjectInstructionsRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await http.PutAsJsonAsync($"api/projects/{projectId}/instructions", request, cancellationToken);
        return await ReadAsync<ProjectInstructions>(response, cancellationToken);
    }

    public async Task<ModelContextPreview> GetModelContextAsync(Guid projectId, CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<ModelContextPreview>($"api/projects/{projectId}/model-context", cancellationToken)
        ?? new ModelContextPreview([], 0);

    private static string Owner(MemoryEntry entry) => entry.ProjectId is { } id ? $"?projectId={id}" : string.Empty;

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new InvalidOperationException("The server returned an empty response.");
    }

    private static async Task EnsureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.Conflict) throw new MemoryConflictException();
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException("It no longer exists.");
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } text)
                throw new InvalidOperationException(text);
        }
        catch (JsonException)
        {
            // Not a problem document; the status line below is all there is to say.
        }
        throw new InvalidOperationException($"The request failed ({(int)response.StatusCode}).");
    }
}
