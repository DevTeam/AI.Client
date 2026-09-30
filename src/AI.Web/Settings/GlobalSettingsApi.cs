namespace AI.Web.Settings;

using AI.Contracts.Settings;
using System.Net.Http.Json;
using System.Text.Json;

public sealed class GlobalSettingsApi(HttpClient httpClient) : IGlobalSettingsApi
{
    public async Task<IReadOnlyList<McpToolInfo>> GetDefaultToolsAsync(CancellationToken cancellationToken) =>
        await httpClient.GetFromJsonAsync<McpToolInfo[]>("api/mcp/default/tools", cancellationToken) ?? [];
    public async Task<GlobalSettings> GetAsync(CancellationToken cancellationToken) =>
        await httpClient.GetFromJsonAsync<GlobalSettings>("api/settings", cancellationToken)
        ?? new GlobalSettings([], [], []);

    public async Task<GlobalSettings> SaveAsync(SaveGlobalSettingsRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync("api/settings", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GlobalSettings>(cancellationToken)
               ?? throw new InvalidOperationException("Global settings response is empty.");
    }

    public async Task<GlobalSettings> SetChatAutomationAsync(ChatAutomationSettings automation, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync("api/settings/chat-automation", automation, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GlobalSettings>(cancellationToken)
               ?? throw new InvalidOperationException("Global settings response is empty.");
    }

    public Task SetConnectionCredentialAsync(Guid id, string? value, CancellationToken cancellationToken) =>
        SetSecretAsync($"api/settings/connections/{id}/credential", value, cancellationToken);

    public Task SetMcpCredentialAsync(Guid id, string? value, CancellationToken cancellationToken) =>
        SetSecretAsync($"api/settings/mcp/{id}/credential", value, cancellationToken);

    public async Task<GlobalSettings> RemoveToolPolicyAsync(Guid serverId, string name, string schemaHash, CancellationToken cancellationToken)
    {
        var url = $"api/settings/mcp/{serverId}/tool-policies?name={Uri.EscapeDataString(name)}&schemaHash={Uri.EscapeDataString(schemaHash)}";
        using var response = await httpClient.DeleteAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GlobalSettings>(cancellationToken)
            ?? throw new InvalidOperationException("Global settings response is empty.");
    }

    public async Task<IReadOnlyList<ResolvedModelInfo>> GetConnectionModelsAsync(
        Guid id, ResolveConnectionModelsRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync($"api/settings/connections/{id}/models", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // The Host explains the failure in the problem's detail ("Could not reach ...", "The
            // endpoint returned 401 ..."); the bare status line would hide the part the user can act on.
            throw new InvalidOperationException(await ReadProblemDetailAsync(response, cancellationToken));
        }

        return await response.Content.ReadFromJsonAsync<ResolvedModelInfo[]>(cancellationToken) ?? [];
    }

    private static async Task<string> ReadProblemDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var fallback = $"The Host returned {(int)response.StatusCode} ({response.ReasonPhrase}).";
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(cancellationToken);
            return string.IsNullOrWhiteSpace(problem?.Detail) ? fallback : problem.Detail;
        }
        catch (JsonException)
        {
            return fallback;
        }
        catch (NotSupportedException)
        {
            // Not JSON at all (an HTML error page from something in between).
            return fallback;
        }
    }

    private sealed record ProblemBody(string? Detail);

    private async Task SetSecretAsync(string url, string? value, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync(url, new UpdateSecretRequest(value), cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
