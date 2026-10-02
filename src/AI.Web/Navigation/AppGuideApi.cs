namespace AI.Web.Navigation;

using System.Net.Http.Json;
using AI.Contracts.Navigation;
using AI.Contracts.Runs;

public interface IAppGuideApi
{
    Task<ChatRunSnapshot> StartAsync(AppGuideStartRequest request, CancellationToken token);
    Task<bool> ClaimAsync(Guid requestId, Guid clientId, CancellationToken token);
    Task CompleteAsync(Guid requestId, AppNavigationDecision decision, CancellationToken token);

    /// <summary>Removes the project's guide service chats whose tour is over.</summary>
    Task CleanUpAsync(Guid projectId, CancellationToken token);
}

public sealed class AppGuideApi(HttpClient http) : IAppGuideApi
{
    public async Task<ChatRunSnapshot> StartAsync(AppGuideStartRequest request, CancellationToken token)
    {
        using var response = await http.PostAsJsonAsync("api/guide/start", request, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatRunSnapshot>(token)
            ?? throw new InvalidOperationException("The guide did not start.");
    }

    public async Task<bool> ClaimAsync(Guid requestId, Guid clientId, CancellationToken token)
    {
        using var response = await http.PostAsJsonAsync($"api/navigation/{requestId}/claim",
            new AppNavigationDecision(clientId, "claim"), token);
        return response.IsSuccessStatusCode;
    }

    public async Task CompleteAsync(Guid requestId, AppNavigationDecision decision, CancellationToken token)
    {
        using var response = await http.PostAsJsonAsync($"api/navigation/{requestId}/complete", decision, token);
        if (response.StatusCode != System.Net.HttpStatusCode.Conflict) response.EnsureSuccessStatusCode();
    }

    public async Task CleanUpAsync(Guid projectId, CancellationToken token)
    {
        using var response = await http.PostAsync($"api/guide/{projectId}/cleanup", null, token);
        response.EnsureSuccessStatusCode();
    }
}
