namespace AI.Web.Chats;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.Contracts.Schedules;

/// <summary>The schedule of a chat, as the schedule widget changes it.</summary>
public interface IChatScheduleApi
{
    Task<ChatScheduleView> SetAsync(Guid projectId, Guid chatId, SetChatScheduleRequest request, CancellationToken cancellationToken);
    Task<ChatScheduleView> PauseAsync(Guid projectId, Guid chatId, bool paused, long? revision, CancellationToken cancellationToken);
    Task<ChatScheduleView> RemoveAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);
    Task<ChatScheduleView> RunNowAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);

    /// <summary>
    /// The scheduled chats of every project the dispatcher will act on within the next day,
    /// soonest first: what the sidebar's Scheduled section lists.
    /// </summary>
    Task<IReadOnlyList<ScheduledChatSummary>> ListSoonAsync(int limit, CancellationToken cancellationToken);

    /// <summary>A chat's schedule state, parsed from its kind state; null for a chat that is not scheduled.</summary>
    ChatSchedule? Parse(string kind, JsonElement? state);
}

/// <summary>What the Host refused, in its own words, with the schedule as it is now when it had changed.</summary>
public sealed class ChatScheduleException(string message, ChatScheduleView? current = null) : Exception(message)
{
    public ChatScheduleView? Current { get; } = current;
}

public sealed class ChatScheduleApi(HttpClient httpClient) : IChatScheduleApi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ChatScheduleView> SetAsync(Guid projectId, Guid chatId, SetChatScheduleRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync(Route(projectId, chatId), request, Json, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    public async Task<ChatScheduleView> PauseAsync(Guid projectId, Guid chatId, bool paused, long? revision,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(
            $"{Route(projectId, chatId)}/{(paused ? "pause" : "resume")}{(revision is { } value ? $"?revision={value}" : string.Empty)}",
            null, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    public async Task<ChatScheduleView> RemoveAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.DeleteAsync(Route(projectId, chatId), cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    public async Task<ChatScheduleView> RunNowAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync($"{Route(projectId, chatId)}/run", null, cancellationToken);
        return await ReadAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ScheduledChatSummary>> ListSoonAsync(int limit, CancellationToken cancellationToken) =>
        await httpClient.GetFromJsonAsync<IReadOnlyList<ScheduledChatSummary>>(
            $"api/scheduled-chats/soon?limit={limit}", cancellationToken) ?? [];

    public ChatSchedule? Parse(string kind, JsonElement? state)
    {
        if (kind != ChatSchedule.Kind || state is not { ValueKind: JsonValueKind.Object } value) return null;
        try
        {
            return value.Deserialize<ChatSchedule>(Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Route(Guid projectId, Guid chatId) => $"api/projects/{projectId}/chats/{chatId}/schedule";

    private static async Task<ChatScheduleView> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadFromJsonAsync<ChatScheduleView>(Json, cancellationToken)
                ?? throw new ChatScheduleException("The schedule response is empty.");
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new ChatScheduleException("The chat no longer exists.");
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            // A stale revision answers with the schedule as it is now; a refusal with a problem detail.
            if (response.StatusCode == HttpStatusCode.Conflict && root.TryGetProperty("chatId", out _))
                throw new ChatScheduleException("The schedule changed meanwhile. Here it is as it is now.",
                    root.Deserialize<ChatScheduleView>(Json));
            if (root.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } message)
                throw new ChatScheduleException(message);
        }
        catch (JsonException)
        {
        }
        throw new ChatScheduleException($"The schedule could not be changed ({(int)response.StatusCode}).");
    }
}
