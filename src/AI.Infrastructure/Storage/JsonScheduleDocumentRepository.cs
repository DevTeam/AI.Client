namespace AI.Infrastructure.Storage;

using System.Text.Json;
using AI.Application.Schedules;
using AI.Contracts.FileSystem;
using AI.Contracts.Schedules;
using AI.Domain.Projects;

/// <summary>
/// Schedules as <c>&lt;chat&gt;.&lt;branch&gt;.schedule.json</c> beside the chat documents, each revised on
/// its own.
/// </summary>
public sealed class JsonScheduleDocumentRepository(IFileSystem files, IChatStoragePaths paths) : IScheduleDocumentRepository
{
    private const string Suffix = ".schedule.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<ChatSchedule?> ReadAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken)
    {
        var text = await files.ReadTextAsync(PathOf(projectId, chatId, branchId), cancellationToken);
        return text is null ? null : JsonSerializer.Deserialize<ChatSchedule>(text, Json);
    }

    public async Task WriteAsync(Guid projectId, Guid chatId, Guid branchId, ChatSchedule schedule,
        CancellationToken cancellationToken)
    {
        var path = PathOf(projectId, chatId, branchId);
        await files.WriteTextAsync(path + ".tmp", JsonSerializer.Serialize(schedule, Json), cancellationToken);
        await files.MoveAsync(path + ".tmp", path, true, cancellationToken);
    }

    public Task DeleteAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken) =>
        files.DeleteFileAsync(PathOf(projectId, chatId, branchId), cancellationToken);

    public async Task<IReadOnlyList<(Guid ChatId, Guid BranchId)>> ListAsync(Guid projectId, CancellationToken cancellationToken) =>
        Owners(await files.ListFilesAsync(Directory(projectId), "*" + Suffix, cancellationToken)).ToArray();

    public async Task<IReadOnlyList<Guid>> ListBranchesAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken) =>
        Owners(await files.ListFilesAsync(Directory(projectId), $"{chatId:N}.*{Suffix}", cancellationToken))
            .Where(owner => owner.ChatId == chatId).Select(owner => owner.BranchId).ToArray();

    private static IEnumerable<(Guid ChatId, Guid BranchId)> Owners(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            var name = Path.GetFileName(file).Split('.');
            if (name.Length == 4 && Guid.TryParseExact(name[0], "N", out var chatId)
                && Guid.TryParseExact(name[1], "N", out var branchId))
                yield return (chatId, branchId);
        }
    }

    private string Directory(Guid projectId) => paths.GetChatsDirectory(new ProjectId(projectId));

    private string PathOf(Guid projectId, Guid chatId, Guid branchId) =>
        Path.Combine(Directory(projectId), $"{chatId:N}.{branchId:N}{Suffix}");
}
