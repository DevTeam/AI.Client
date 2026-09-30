namespace AI.Web.Usage;

using System.Net.Http.Json;
using AI.Contracts.Usage;

public sealed class ChatUsageApi(HttpClient httpClient) : IChatUsageApi
{
    public async Task<ChatTokenUsage?> GetChatAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"api/projects/{projectId}/chats/{chatId}/usage", cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<ChatTokenUsage>(cancellationToken)
            : null;
    }
}
