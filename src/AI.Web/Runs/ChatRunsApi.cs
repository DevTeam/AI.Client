namespace AI.Web.Runs;

using AI.Contracts.Runs;
using System.Net;
using System.Net.Http.Json;

public sealed class ChatRunsApi(HttpClient httpClient) : IChatRunsApi
{
    public async Task DecideToolAsync(Guid projectId, Guid chatId, Guid branchId, ToolApprovalDecision decision, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync($"api/projects/{projectId}/chats/{chatId}/tools/decision?branchId={branchId}", decision, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
    public async Task<bool> AnswerPromptAsync(Guid projectId, Guid chatId, Guid branchId, UserPromptResponse response, CancellationToken cancellationToken)
    {
        using var result = await httpClient.PostAsJsonAsync(
            $"api/projects/{projectId}/chats/{chatId}/prompts/answer?branchId={branchId}", response, cancellationToken);
        if (result.StatusCode == HttpStatusCode.Conflict) return false;
        result.EnsureSuccessStatusCode();
        return true;
    }

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
        return await ReadSnapshotAsync(response, cancellationToken);
    }
    public async Task<ChatRunSnapshot?> RemoveQueuedAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, Guid operationId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.DeleteAsync($"api/projects/{projectId}/chats/{chatId}/queue/{messageId}{BranchQuery(branchId)}&operationId={operationId}", cancellationToken);
        return await ReadSnapshotAsync(response, cancellationToken);
    }
    public async Task<ChatRunSnapshot?> SendQueuedNowAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, Guid operationId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync($"api/projects/{projectId}/chats/{chatId}/queue/{messageId}/send-now{BranchQuery(branchId)}&operationId={operationId}", null, cancellationToken);
        return await ReadSnapshotAsync(response, cancellationToken);
    }
    public Task<ChatRunSnapshot?> ResumeAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken) => PostCommandAsync(projectId, chatId, "resume", branchId, operationId, cancellationToken);
    public Task<ChatRunSnapshot?> SkipFailedAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken) => PostCommandAsync(projectId, chatId, "queue/skip", branchId, operationId, cancellationToken);
    public Task<ChatRunSnapshot?> RebaseAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken) => PostCommandAsync(projectId, chatId, "queue/rebase", branchId, operationId, cancellationToken);
    public Task<ChatRunSnapshot?> ClearAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken) => PostCommandAsync(projectId, chatId, "queue/clear", branchId, operationId, cancellationToken);
    public Task<ChatRunSnapshot?> ClearAllAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken) => PostCommandAsync(projectId, chatId, "queue/clear-all", branchId, operationId, cancellationToken);
    public Task<ChatRunSnapshot?> DiscardAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken) => PostCommandAsync(projectId, chatId, "queue/discard", branchId, operationId, cancellationToken);
    public async Task<ChatReplySuggestion?> GetReplySuggestionAsync(Guid projectId, Guid chatId, Guid branchId,
        Guid leafMessageId, bool generate, CancellationToken cancellationToken)
    {
        var url = $"api/projects/{projectId}/chats/{chatId}/reply-suggestion?branchId={branchId}&leafMessageId={leafMessageId}";
        using var response = generate
            ? await httpClient.PostAsync(url, null, cancellationToken)
            : await httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK) return null;
        return await response.Content.ReadFromJsonAsync<ChatReplySuggestion>(cancellationToken);
    }
    private async Task<ChatRunSnapshot?> PostCommandAsync(Guid projectId, Guid chatId, string command, Guid branchId, Guid operationId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(CommandUrl(projectId, chatId, command, branchId, operationId), null, cancellationToken);
        return await ReadSnapshotAsync(response, cancellationToken);
    }

    /// <summary>
    /// A refused queue command carries the reason the server gave, and the caller is expected to
    /// show it. Swallowing it is how "the button does nothing and I can't tell why" happened: the
    /// server answered "this message has already been sent", and the answer was dropped here. A
    /// 404 still returns null — the run is simply gone, which is not something to report.
    /// </summary>
    private static async Task<ChatRunSnapshot?> ReadSnapshotAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return await response.Content.ReadFromJsonAsync<ChatRunSnapshot>(cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        throw new InvalidOperationException(await DescribeAsync(response, cancellationToken));
    }

    private static async Task<string> DescribeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(cancellationToken);
            if (problem?.Detail is { Length: > 0 } detail) return detail;
            if (problem?.Title is { Length: > 0 } title) return title;
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or HttpRequestException or NotSupportedException) { }
        return $"The queue command was refused ({(int)response.StatusCode}).";
    }

    private sealed record ProblemResponse(string? Title, string? Detail);
    private static string CommandUrl(Guid projectId, Guid chatId, string command, Guid branchId, Guid operationId)
    {
        return $"api/projects/{projectId}/chats/{chatId}/{command}?branchId={branchId}&operationId={operationId}";
    }

    private static string BranchQuery(Guid branchId) => $"?branchId={branchId}";
}
