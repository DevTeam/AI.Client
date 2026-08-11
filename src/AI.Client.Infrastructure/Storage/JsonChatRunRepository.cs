using System.Text.Json;
using AI.Client.Application.Runs;
using AI.Client.Domain.Runs;

namespace AI.Client.Infrastructure.Storage;

public sealed class JsonChatRunRepository(ITextFileSystem fileSystem, ChatRunStoragePaths paths) : IChatRunRepository
{
    private const int SchemaVersion = 3;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public async Task<ChatRunState?> GetAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken)
    {
        var json = await fileSystem.ReadTextAsync(paths.GetPath(projectId, chatId, branchId), cancellationToken);
        return json is null ? null : TryRestore(json);
    }
    public async Task SaveAsync(ChatRunState state, CancellationToken cancellationToken)
    {
        var document = new Document(SchemaVersion, state.ProjectId, state.ChatId, state.BranchId, state.Status, state.StreamingContent, state.Error, state.HasUnreadResponse, state.Revision, state.Queue.ToArray(), state.Operations.ToArray());
        var temp = paths.GetTemporaryPath(state.ProjectId, state.ChatId, state.BranchId);
        await fileSystem.WriteTextAsync(temp, JsonSerializer.Serialize(document, Options), cancellationToken);
        await fileSystem.MoveAsync(temp, paths.GetPath(state.ProjectId, state.ChatId, state.BranchId), true, cancellationToken);
    }
    public async Task<IReadOnlyList<ChatRunState>> ListAsync(CancellationToken cancellationToken)
    {
        var files = Directory.Exists(paths.RootDirectory) ? Directory.GetFiles(paths.RootDirectory, "*.run.json", SearchOption.AllDirectories) : [];
        var result = new List<ChatRunState>();
        foreach (var file in files)
        {
            var json = await fileSystem.ReadTextAsync(file, cancellationToken);
            var state = json is null ? null : TryRestore(json);
            if (state is not null) result.Add(state);
        }
        return result;
    }
    public Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        DeleteFilesAsync(paths.GetChatsDirectory(projectId), "*.run.json", null, cancellationToken);
    public Task DeleteChatAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken) =>
        DeleteFilesAsync(paths.GetChatsDirectory(projectId), $"{chatId:N}.*.run.json", null, cancellationToken);
    public Task DeleteExceptAsync(Guid projectId, Guid chatId, IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken) =>
        DeleteFilesAsync(paths.GetChatsDirectory(projectId), $"{chatId:N}.*.run.json", branchIds, cancellationToken);
    private async Task DeleteFilesAsync(string directory, string pattern, IReadOnlySet<Guid>? retainedBranchIds, CancellationToken cancellationToken)
    {
        var files = await fileSystem.ListFilesAsync(directory, pattern, cancellationToken);
        foreach (var file in files)
        {
            if (retainedBranchIds is not null)
            {
                var json = await fileSystem.ReadTextAsync(file, cancellationToken);
                var state = json is null ? null : TryRestore(json);
                if (state is not null && retainedBranchIds.Contains(state.BranchId)) continue;
            }
            await fileSystem.DeleteAsync(file, cancellationToken);
        }
    }
    private static ChatRunState? TryRestore(string json)
    {
        try
        {
            var document = JsonSerializer.Deserialize<Document>(json, Options);
            return document is not { SchemaVersion: SchemaVersion }
                || document.ProjectId == Guid.Empty
                || document.ChatId == Guid.Empty
                || document.BranchId == Guid.Empty
                || document.Queue is null
                || document.Operations is null
                    ? null
                    : Restore(document);
        }
        catch (JsonException)
        {
            return null;
        }
    }
    private static ChatRunState Restore(Document document) => ChatRunState.Restore(
        document.ProjectId, document.ChatId, document.BranchId, document.Status, document.StreamingContent, document.Error,
        document.HasUnreadResponse, document.Revision, document.Queue, document.Operations);
    private sealed record Document(int SchemaVersion, Guid ProjectId, Guid ChatId, Guid BranchId, RunStatus Status, string StreamingContent, string? Error, bool HasUnreadResponse, long Revision, QueuedRunMessage[] Queue, Guid[] Operations);
}
