namespace AI.Web.Usage;

using System.Net.Http.Json;
using AI.Contracts.Chats;

public interface IHistoryCheckpointApi
{
    Task<IReadOnlyList<HistoryCheckpoint>> ListAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);

    /// <summary>What compacting the branch did; a refusal comes back as a status, not as an exception.</summary>
    Task<HistoryCompactionResponse> CompactAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid projectId, Guid chatId, Guid checkpointId, CancellationToken cancellationToken);
}

public sealed class HistoryCheckpointApi(HttpClient httpClient) : IHistoryCheckpointApi
{
    public async Task<IReadOnlyList<HistoryCheckpoint>> ListAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken) =>
        await httpClient.GetFromJsonAsync<IReadOnlyList<HistoryCheckpoint>>(
            $"api/projects/{projectId}/chats/{chatId}/history-checkpoints", cancellationToken) ?? [];

    public async Task<HistoryCompactionResponse> CompactAsync(Guid projectId, Guid chatId, Guid branchId,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(
            $"api/projects/{projectId}/chats/{chatId}/branches/{branchId}/compact", null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new HistoryCompactionResponse(HistoryCompactionStatus.Failed, Error: "This branch no longer exists.");
        try
        {
            return await response.Content.ReadFromJsonAsync<HistoryCompactionResponse>(cancellationToken)
                   ?? new HistoryCompactionResponse(HistoryCompactionStatus.Failed, Error: "The Host sent no answer.");
        }
        catch (System.Text.Json.JsonException)
        {
            return new HistoryCompactionResponse(HistoryCompactionStatus.Failed,
                Error: $"The Host answered {(int)response.StatusCode}.");
        }
    }

    public async Task<bool> DeleteAsync(Guid projectId, Guid chatId, Guid checkpointId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.DeleteAsync(
            $"api/projects/{projectId}/chats/{chatId}/history-checkpoints/{checkpointId}", cancellationToken);
        return response.IsSuccessStatusCode;
    }
}
