namespace AI.Web.Chats;

using AI.Contracts.Chats;
using System.Net;
using System.Net.Http.Json;

public sealed class ChatHistoryApi(HttpClient httpClient) : IChatHistoryApi
{
    public async Task<IReadOnlyList<ChatSummary>> ListAsync(Guid projectId, CancellationToken cancellationToken) =>
        await httpClient.GetFromJsonAsync<IReadOnlyList<ChatSummary>>($"api/projects/{projectId}/chats", cancellationToken) ?? [];

    public async Task<ChatSearchResult> SearchAsync(string query, Guid? projectId, CancellationToken cancellationToken)
    {
        var scope = projectId is { } id ? $"&projectId={id}" : string.Empty;
        return await httpClient.GetFromJsonAsync<ChatSearchResult>(
            $"api/chats/search?query={Uri.EscapeDataString(query)}{scope}", cancellationToken)
            ?? new ChatSearchResult([], 0, 0, false, null);
    }

    public async Task<ChatDetails?> GetAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"api/projects/{projectId}/chats/{chatId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatDetails>(cancellationToken);
    }

    public async Task<ChatDetails?> GetTranscriptAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"api/projects/{projectId}/chats/{chatId}/transcript", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatDetails>(cancellationToken);
    }

    public async Task<ChatTurnActivity?> GetTurnActivityAsync(
        Guid projectId,
        Guid chatId,
        Guid turnId,
        Guid branchLeafId,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            $"api/projects/{projectId}/chats/{chatId}/turns/{turnId}/activity?branchLeafId={branchLeafId}",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatTurnActivity>(cancellationToken);
    }

    public async Task<ChatMessageContent?> GetMessageContentAsync(
        Guid projectId,
        Guid chatId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            $"api/projects/{projectId}/chats/{chatId}/messages/{messageId}/content",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatMessageContent>(cancellationToken);
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

    public async Task<ChatSummary?> PinAsync(
        Guid projectId,
        Guid chatId,
        PinChatRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync(
            $"api/projects/{projectId}/chats/{chatId}/pin",
            request,
            cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<ChatSummary>(cancellationToken);
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
        // The Host returns NoContent on success (no body), NotFound for missing chat, and Conflict
        // with a body describing the current revision. Only parse the body when one exists — the
        // default `ReadFromJsonAsync` would throw on an empty stream, which was happening in the
        // browser when the chat had already been deleted server-side.
        if (response.IsSuccessStatusCode) return new ChatDeleteResult(true, revision);
        if (response.StatusCode == HttpStatusCode.NotFound) return new ChatDeleteResult(false, 0);
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
        return await response.Content.ReadFromJsonAsync<ChatBranchDeleteResult>(cancellationToken)
            ?? new ChatBranchDeleteResult(false, 0, null, null);
    }

    public async Task<ChatDetails?> SetToolPolicyAsync(Guid projectId, Guid chatId,
        AI.Contracts.Projects.ToolPolicySettings policy, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync($"api/projects/{projectId}/chats/{chatId}/tool-policies", policy, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<ChatDetails>(cancellationToken);
    }

    public async Task<ChatDetails?> RemoveToolPolicyAsync(Guid projectId, Guid chatId, Guid serverId,
        string name, string schemaHash, CancellationToken cancellationToken)
    {
        var url = $"api/projects/{projectId}/chats/{chatId}/tool-policies/{serverId}?name={Uri.EscapeDataString(name)}&schemaHash={Uri.EscapeDataString(schemaHash)}";
        using var response = await httpClient.DeleteAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<ChatDetails>(cancellationToken);
    }
}
