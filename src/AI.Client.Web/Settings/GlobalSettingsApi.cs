using AI.Client.Contracts.Settings;
using System.Net.Http.Json;

namespace AI.Client.Web.Settings;

public sealed class GlobalSettingsApi(HttpClient httpClient) : IGlobalSettingsApi
{
    public async Task<GlobalSettings> GetAsync(CancellationToken cancellationToken) =>
        await httpClient.GetFromJsonAsync<GlobalSettings>("api/settings", cancellationToken)
        ?? new GlobalSettings([], []);

    public async Task<GlobalSettings> SaveAsync(SaveGlobalSettingsRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync("api/settings", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GlobalSettings>(cancellationToken)
               ?? throw new InvalidOperationException("Global settings response is empty.");
    }

    public Task SetConnectionCredentialAsync(Guid id, string? value, CancellationToken cancellationToken) =>
        SetSecretAsync($"api/settings/connections/{id}/credential", value, cancellationToken);

    public Task SetMcpCredentialAsync(Guid id, string? value, CancellationToken cancellationToken) =>
        SetSecretAsync($"api/settings/mcp/{id}/credential", value, cancellationToken);

    private async Task SetSecretAsync(string url, string? value, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync(url, new UpdateSecretRequest(value), cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
