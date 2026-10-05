namespace AI.Web.Settings;

using AI.Contracts.Settings;
using System.Net.Http.Json;
using System.Text.Json;

public sealed class GlobalSettingsApi(HttpClient httpClient) : IGlobalSettingsApi
{
    public async Task<IReadOnlyList<McpToolInfo>> GetDefaultToolsAsync(CancellationToken cancellationToken) =>
        await httpClient.GetFromJsonAsync<McpToolInfo[]>("api/mcp/default/tools", cancellationToken) ?? [];

    public async Task<IReadOnlyList<McpToolInfo>> DiscoverMcpToolsAsync(
        DiscoverMcpToolsRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("api/mcp/tools/discover", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadProblemDetailAsync(response, cancellationToken));
        return await response.Content.ReadFromJsonAsync<McpToolInfo[]>(cancellationToken) ?? [];
    }
    public async Task<GlobalSettings> GetAsync(CancellationToken cancellationToken) =>
        await httpClient.GetFromJsonAsync<GlobalSettings>("api/settings", cancellationToken)
        ?? new GlobalSettings([], [], []);

    public Task<GlobalSettings> UpsertConnectionAsync(ConnectionSettings item, ConnectionSettings? expected,
        CancellationToken cancellationToken) => PutSettingsItemAsync($"api/settings/connections/{item.Id}",
        new SettingsItemChange<ConnectionSettings>(item, expected), cancellationToken);

    public Task<GlobalSettings> RemoveConnectionAsync(Guid id, ConnectionSettings expected,
        CancellationToken cancellationToken) => PostSettingsItemAsync($"api/settings/connections/{id}/remove",
        new SettingsItemRemoval<ConnectionSettings>(expected), cancellationToken);

    public Task<GlobalSettings> UpsertMcpServerAsync(McpServerSettings item, McpServerSettings? expected,
        CancellationToken cancellationToken) => PutSettingsItemAsync($"api/settings/mcp/{item.Id}",
        new SettingsItemChange<McpServerSettings>(item, expected), cancellationToken);

    public Task<GlobalSettings> RemoveMcpServerAsync(Guid id, McpServerSettings expected,
        CancellationToken cancellationToken) => PostSettingsItemAsync($"api/settings/mcp/{id}/remove",
        new SettingsItemRemoval<McpServerSettings>(expected), cancellationToken);

    public Task<GlobalSettings> SetToolPolicyAsync(McpToolPolicySettings policy, CancellationToken cancellationToken) =>
        PutSettingsItemAsync($"api/settings/mcp/{policy.ServerId}/tool-policies", policy, cancellationToken);

    private async Task<GlobalSettings> PutSettingsItemAsync<T>(string url, T request, CancellationToken token)
    {
        using var response = await httpClient.PutAsJsonAsync(url, request, token);
        return await ReadSettingsItemResponseAsync(response, token);
    }

    private async Task<GlobalSettings> PostSettingsItemAsync<T>(string url, T request, CancellationToken token)
    {
        using var response = await httpClient.PostAsJsonAsync(url, request, token);
        return await ReadSettingsItemResponseAsync(response, token);
    }

    private static async Task<GlobalSettings> ReadSettingsItemResponseAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadProblemDetailAsync(response, token));
        return await response.Content.ReadFromJsonAsync<GlobalSettings>(token)
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

    public async Task<ImageProbeResult> TestConnectionImageAsync(Guid id, ImageProbeRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync($"api/settings/connections/{id}/image-test", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadProblemDetailAsync(response, cancellationToken));
        return await response.Content.ReadFromJsonAsync<ImageProbeResult>(cancellationToken)
            ?? throw new InvalidOperationException("The image test returned no result.");
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
