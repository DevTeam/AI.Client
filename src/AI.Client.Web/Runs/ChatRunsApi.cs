namespace AI.Client.Web.Runs;

using AI.Client.Contracts.Runs;
using System.Net.Http.Json;

public sealed class ChatRunsApi(HttpClient httpClient) : IChatRunsApi
{
    public async Task<ChatRunSnapshot> SubmitAsync(Guid projectId, Guid chatId, SubmitChatMessageRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync($"api/projects/{projectId}/chats/{chatId}/submit", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatRunSnapshot>(cancellationToken)
            ?? throw new InvalidOperationException("Run response is empty.");
    }

    public async Task<IReadOnlyList<ChatRunSnapshot>> GetAsync(CancellationToken cancellationToken) =>
        await httpClient.GetFromJsonAsync<IReadOnlyList<ChatRunSnapshot>>("api/runs", cancellationToken) ?? [];
    public async Task<ChatRunSnapshot?> StopAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(CommandUrl(projectId, chatId, "stop", branchId, operationId), null, cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<ChatRunSnapshot>(cancellationToken) : null;
    }
    public async Task<ChatRunSnapshot?> MarkReadAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(CommandUrl(projectId, chatId, "read", branchId, operationId), null, cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<ChatRunSnapshot>(cancellationToken) : null;
    }
    public async Task<ChatRunSnapshot?> UpdateQueuedAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, UpdateQueuedMessageRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync($"api/projects/{projectId}/chats/{chatId}/queue/{messageId}{BranchQuery(branchId)}", request, cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<ChatRunSnapshot>(cancellationToken) : null;
    }
    public async Task<ChatRunSnapshot?> RemoveQueuedAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, Guid operationId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.DeleteAsync($"api/projects/{projectId}/chats/{chatId}/queue/{messageId}{BranchQuery(branchId)}&operationId={operationId}", cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<ChatRunSnapshot>(cancellationToken) : null;
    }
    public Task<ChatRunSnapshot?> ResumeAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken) => PostCommandAsync(projectId, chatId, "resume", branchId, operationId, cancellationToken);
    public Task<ChatRunSnapshot?> ClearAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken) => PostCommandAsync(projectId, chatId, "queue/clear", branchId, operationId, cancellationToken);
    private async Task<ChatRunSnapshot?> PostCommandAsync(Guid projectId, Guid chatId, string command, Guid branchId, Guid operationId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(CommandUrl(projectId, chatId, command, branchId, operationId), null, cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<ChatRunSnapshot>(cancellationToken) : null;
    }
    private static string CommandUrl(Guid projectId, Guid chatId, string command, Guid branchId, Guid operationId)
    {
        return $"api/projects/{projectId}/chats/{chatId}/{command}?branchId={branchId}&operationId={operationId}";
    }

    private static string BranchQuery(Guid branchId) => $"?branchId={branchId}";
}
