namespace AI.Application.Schedules;

using System.Text.Json;
using AI.Application.Chats;
using AI.Contracts.FileSystem;
using AI.Contracts.Schedules;
using AI.Infrastructure.Storage;

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

/// <summary>One independently revised schedule file per branch. The chat document remains the
/// authority for branch existence; orphaned files are removed during the scheduler scan.</summary>
public sealed class ChatScheduleStore(IFileSystem files, IChatStoragePaths paths, IChatService chats,
    IChatSynchronization synchronization, IScheduleCalendar calendar) : IChatScheduleStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ChatSchedule? Parse(JsonElement? state) => state is { ValueKind: JsonValueKind.Object } value
        ? value.Deserialize<ChatSchedule>(Json) : null;

    public async Task<StoredSchedule?> ReadAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken)
    {
        var chat = await chats.GetAsync(projectId, chatId, cancellationToken);
        if (chat is null || chat.Branches?.All(branch => branch.Id != branchId) == true) return null;
        var schedule = await ReadFileAsync(projectId, chatId, branchId, cancellationToken);
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
            var path = Path(projectId, chatId, branchId);
            await files.WriteTextAsync(path + ".tmp", JsonSerializer.Serialize(next, Json), cancellationToken);
            await files.MoveAsync(path + ".tmp", path, true, cancellationToken);
            return current with { Schedule = next };
        }
        return current;
    }

    public async Task<StoredSchedule?> RemoveAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken)
    {
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        var current = await ReadAsync(projectId, chatId, branchId, cancellationToken);
        if (current is null) return null;
        await files.DeleteFileAsync(Path(projectId, chatId, branchId), cancellationToken);
        return current with { Schedule = null };
    }

    public async Task<IReadOnlyList<StoredScheduleOwner>> ListAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var listed = new List<StoredScheduleOwner>();
        var chatsById = new Dictionary<Guid, AI.Contracts.Chats.ChatDetails?>();
        foreach (var file in await files.ListFilesAsync(paths.GetChatsDirectory(new AI.Domain.Projects.ProjectId(projectId)),
                     "*.schedule.json", cancellationToken))
        {
            var name = System.IO.Path.GetFileName(file).Split('.');
            if (name.Length != 4 || !Guid.TryParseExact(name[0], "N", out var chatId)
                || !Guid.TryParseExact(name[1], "N", out var branchId)) continue;
            if (!chatsById.TryGetValue(chatId, out var chat))
            {
                chat = await chats.GetAsync(projectId, chatId, cancellationToken);
                chatsById.Add(chatId, chat);
            }
            if (chat is null || chat.Branches?.All(branch => branch.Id != branchId) == true)
            {
                await files.DeleteFileAsync(file, cancellationToken);
                continue;
            }
            if (await ReadFileAsync(projectId, chatId, branchId, cancellationToken) is { } schedule)
                listed.Add(new StoredScheduleOwner(projectId, chatId, branchId, schedule));
        }
        return listed;
    }

    public async Task DeleteExceptAsync(Guid projectId, Guid chatId, IReadOnlySet<Guid>? retainedBranches,
        CancellationToken cancellationToken)
    {
        foreach (var file in await files.ListFilesAsync(paths.GetChatsDirectory(new AI.Domain.Projects.ProjectId(projectId)),
                     $"{chatId:N}.*.schedule.json", cancellationToken))
        {
            var name = System.IO.Path.GetFileName(file).Split('.');
            if (name.Length == 4 && Guid.TryParseExact(name[0], "N", out var ownerChatId) && ownerChatId == chatId
                && Guid.TryParseExact(name[1], "N", out var branchId)
                && (retainedBranches is null || !retainedBranches.Contains(branchId)))
                await files.DeleteFileAsync(file, cancellationToken);
        }
    }

    private async Task<ChatSchedule?> ReadFileAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken)
    {
        var text = await files.ReadTextAsync(Path(projectId, chatId, branchId), cancellationToken);
        if (text is null) return null;
        var schedule = JsonSerializer.Deserialize<ChatSchedule>(text, Json);
        return schedule is { Settings.TimeZone: { Length: > 0 } zone }
            ? schedule with { Settings = schedule.Settings with { TimeZone = calendar.CanonicalTimeZoneId(zone) } }
            : schedule;
    }

    private string Path(Guid projectId, Guid chatId, Guid branchId) => System.IO.Path.Combine(
        paths.GetChatsDirectory(new AI.Domain.Projects.ProjectId(projectId)),
        $"{chatId:N}.{branchId:N}.schedule.json");
}
