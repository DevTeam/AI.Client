namespace AI.Application.Schedules;

using System.Text.Json;
using AI.Application.Chats;
using AI.Contracts.Schedules;

public interface IChatScheduleStore
{
    Task<StoredSchedule?> ReadAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken);
    Task<StoredSchedule?> UpdateAsync(Guid projectId, Guid chatId, Guid branchId,
        Func<StoredSchedule, ChatSchedule?> change, CancellationToken cancellationToken);
    Task<StoredSchedule?> RemoveAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredScheduleOwner>> ListAsync(Guid projectId, CancellationToken cancellationToken);
    Task DeleteExceptAsync(Guid projectId, Guid chatId, IReadOnlySet<Guid>? retainedBranches, CancellationToken cancellationToken);
    ChatSchedule? Parse(JsonElement? state);
}

public sealed record StoredSchedule(string Kind, ChatSchedule? Schedule, long ChatRevision);
public sealed record StoredScheduleOwner(Guid ProjectId, Guid ChatId, Guid BranchId, ChatSchedule Schedule);

/// <summary>One independently revised schedule document per branch. The chat document remains the
/// authority for branch existence; orphaned schedules are removed during the scheduler scan.</summary>
public sealed class ChatScheduleStore(IScheduleDocumentRepository documents, IChatService chats,
    IChatSynchronization synchronization, IScheduleCalendar calendar) : IChatScheduleStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ChatSchedule? Parse(JsonElement? state) => state is { ValueKind: JsonValueKind.Object } value
        ? value.Deserialize<ChatSchedule>(Json) : null;

    public async Task<StoredSchedule?> ReadAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken)
    {
        var chat = await chats.GetAsync(projectId, chatId, cancellationToken);
        if (chat is null || chat.Branches?.All(branch => branch.Id != branchId) == true) return null;
        var schedule = await ReadDocumentAsync(projectId, chatId, branchId, cancellationToken);
        return new StoredSchedule(chat.Kind, schedule, chat.Revision);
    }

    public async Task<StoredSchedule?> UpdateAsync(Guid projectId, Guid chatId, Guid branchId,
        Func<StoredSchedule, ChatSchedule?> change, CancellationToken cancellationToken)
    {
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        var current = await ReadAsync(projectId, chatId, branchId, cancellationToken);
        if (current is null) return null;
        if (change(current) is { } next)
        {
            await documents.WriteAsync(projectId, chatId, branchId, next, cancellationToken);
            return current with { Schedule = next };
        }
        return current;
    }

    public async Task<StoredSchedule?> RemoveAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken)
    {
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        var current = await ReadAsync(projectId, chatId, branchId, cancellationToken);
        if (current is null) return null;
        await documents.DeleteAsync(projectId, chatId, branchId, cancellationToken);
        return current with { Schedule = null };
    }

    public async Task<IReadOnlyList<StoredScheduleOwner>> ListAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var listed = new List<StoredScheduleOwner>();
        var chatsById = new Dictionary<Guid, AI.Contracts.Chats.ChatDetails?>();
        foreach (var (chatId, branchId) in await documents.ListAsync(projectId, cancellationToken))
        {
            if (!chatsById.TryGetValue(chatId, out var chat))
            {
                chat = await chats.GetAsync(projectId, chatId, cancellationToken);
                chatsById.Add(chatId, chat);
            }
            if (chat is null || chat.Branches?.All(branch => branch.Id != branchId) == true)
            {
                await documents.DeleteAsync(projectId, chatId, branchId, cancellationToken);
                continue;
            }
            if (await ReadDocumentAsync(projectId, chatId, branchId, cancellationToken) is { } schedule)
                listed.Add(new StoredScheduleOwner(projectId, chatId, branchId, schedule));
        }
        return listed;
    }

    public async Task DeleteExceptAsync(Guid projectId, Guid chatId, IReadOnlySet<Guid>? retainedBranches,
        CancellationToken cancellationToken)
    {
        foreach (var branchId in await documents.ListBranchesAsync(projectId, chatId, cancellationToken))
            if (retainedBranches is null || !retainedBranches.Contains(branchId))
                await documents.DeleteAsync(projectId, chatId, branchId, cancellationToken);
    }

    private async Task<ChatSchedule?> ReadDocumentAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken)
    {
        var schedule = await documents.ReadAsync(projectId, chatId, branchId, cancellationToken);
        return schedule is { Settings.TimeZone: { Length: > 0 } zone }
            ? schedule with { Settings = schedule.Settings with { TimeZone = calendar.CanonicalTimeZoneId(zone) } }
            : schedule;
    }
}
