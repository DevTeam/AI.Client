namespace AI.Client.Web.Chats;

using AI.Client.Contracts.Chats;
using System.Net;
using System.Net.Http.Json;

public sealed class ChatHistoryApi(HttpClient httpClient) : IChatHistoryApi
{
    public async Task<IReadOnlyList<ChatSummary>> ListAsync(Guid projectId, CancellationToken cancellationToken) =>
        await httpClient.GetFromJsonAsync<IReadOnlyList<ChatSummary>>($"api/projects/{projectId}/chats", cancellationToken) ?? [];

    public async Task<ChatDetails?> GetAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"api/projects/{projectId}/chats/{chatId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatDetails>(cancellationToken);
    }

    public async Task<ChatDetails> CreateAsync(Guid projectId, CreateChatRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync($"api/projects/{projectId}/chats", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatDetails>(cancellationToken)
            ?? throw new InvalidOperationException("Chat creation response is empty.");
    }

    public async Task<ChatDetails?> UpdateEndpointAsync(
        Guid projectId,
        Guid chatId,
        UpdateChatEndpointRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync(
            $"api/projects/{projectId}/chats/{chatId}/endpoint",
            request,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatDetails>(cancellationToken);
    }

    public async Task<ChatDetails?> RenameAsync(
        Guid projectId,
        Guid chatId,
        RenameChatRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync(
            $"api/projects/{projectId}/chats/{chatId}/title",
            request,
            cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<ChatDetails>(cancellationToken);
    }

    public async Task<ChatDeleteResult> DeleteAsync(
        Guid projectId,
        Guid chatId,
        long revision,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.DeleteAsync(
            $"api/projects/{projectId}/chats/{chatId}?revision={revision}",
            cancellationToken);
        if (response.IsSuccessStatusCode) return new ChatDeleteResult(true, revision);
        return await response.Content.ReadFromJsonAsync<ChatDeleteResult>(cancellationToken)
               ?? new ChatDeleteResult(false, 0);
    }

    public async Task<ChatDetails?> RenameBranchAsync(Guid projectId, Guid chatId, Guid branchId, RenameChatBranchRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync($"api/projects/{projectId}/chats/{chatId}/branches/{branchId}/title", request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<ChatDetails>(cancellationToken);
    }

    public async Task<ChatBranchDeleteResult> DeleteBranchAsync(Guid projectId, Guid chatId, Guid branchId, long revision, CancellationToken cancellationToken)
    {
        using var response = await httpClient.DeleteAsync($"api/projects/{projectId}/chats/{chatId}/branches/{branchId}?revision={revision}", cancellationToken);
        return await response.Content.ReadFromJsonAsync<ChatBranchDeleteResult>(cancellationToken) ?? new ChatBranchDeleteResult(false, 0, null);
    }
}
