namespace AI.Application.Schedules;

using System.Text.Json;
using AI.Application.Chats;
using AI.Contracts.Schedules;

/// <summary>
/// Reads and writes a chat's schedule, which is the state of its <c>scheduled</c> kind. Every write
/// is a function of the latest stored schedule, applied under the chat's lease: the widget, the
/// tools and the dispatcher change different parts of it, and none of them may undo another.
/// </summary>
public interface IChatScheduleStore
{
    /// <summary>The chat's kind and, for a scheduled chat, its schedule; null when the chat does not exist.</summary>
    Task<StoredSchedule?> ReadAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the schedule with <paramref name="change"/> of the stored one. The change gets null
    /// for a chat that is not scheduled yet; returning null leaves the chat as it is. Null when the
    /// chat does not exist.
    /// </summary>
    Task<StoredSchedule?> UpdateAsync(Guid projectId, Guid chatId, Func<StoredSchedule, ChatSchedule?> change,
        CancellationToken cancellationToken);

    /// <summary>Turns a scheduled chat back into a conversation, keeping its messages and branches.</summary>
    Task<StoredSchedule?> RemoveAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);

    ChatSchedule? Parse(JsonElement? state);

    JsonElement Serialize(ChatSchedule schedule);
}

/// <param name="Schedule">Null for a chat that is not scheduled.</param>
public sealed record StoredSchedule(string Kind, ChatSchedule? Schedule, long ChatRevision);

public sealed class ChatScheduleStore(IChatService chats, IScheduleCalendar calendar) : IChatScheduleStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<StoredSchedule?> ReadAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken) =>
        await chats.ChangeKindAsync(projectId, chatId, _ => null, cancellationToken) is { } read ? ToStored(read) : null;

    public async Task<StoredSchedule?> UpdateAsync(Guid projectId, Guid chatId, Func<StoredSchedule, ChatSchedule?> change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        var result = await chats.ChangeKindAsync(projectId, chatId, current =>
            change(ToStored(current, 0)) is { } next
                ? new ChatKindState(ChatSchedule.Kind, Serialize(next), ChatSchedule.StateVersion)
                : null, cancellationToken);
        return result is null ? null : ToStored(result);
    }

    public async Task<StoredSchedule?> RemoveAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        var result = await chats.ChangeKindAsync(projectId, chatId, current => current.Kind == ChatSchedule.Kind
            ? new ChatKindState("conversation", null, 1) : null, cancellationToken);
        return result is null ? null : ToStored(result);
    }

    public ChatSchedule? Parse(JsonElement? state)
    {
        if (state is not { ValueKind: JsonValueKind.Object } value) return null;
        try
        {
            // Schedules saved before zones were stored as IANA ids may name a Windows one.
            return value.Deserialize<ChatSchedule>(Json) is { Settings.TimeZone: { Length: > 0 } zone } schedule
                ? schedule with { Settings = schedule.Settings with { TimeZone = calendar.CanonicalTimeZoneId(zone) } }
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public JsonElement Serialize(ChatSchedule schedule) => JsonSerializer.SerializeToElement(schedule, Json);

    private StoredSchedule ToStored(ChatKindChange change) => ToStored(change.Current, change.Revision);

    private StoredSchedule ToStored(ChatKindState state, long revision) =>
        new(state.Kind, state.Kind == ChatSchedule.Kind ? Parse(state.State) : null, revision);
}
