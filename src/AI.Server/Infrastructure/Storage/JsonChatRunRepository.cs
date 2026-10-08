// ReSharper disable UseCollectionExpression
namespace AI.Infrastructure.Storage;

using AI.Contracts.FileSystem;
using Application.Runs;
using Domain.Runs;
using System.Text.Json;

public sealed class JsonChatRunRepository(IFileSystem fileSystem, IChatRunStoragePaths paths) : IPersistentChatRunRepository, IDisposable
{
    public void Dispose() => _writes.Dispose();

    private readonly AsyncGate _writes = new();
    private const int SchemaVersion = 5;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public async Task<ChatRunState?> GetAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken)
    {
        var json = await fileSystem.ReadTextAsync(paths.GetPath(projectId, chatId, branchId), cancellationToken);
        return json is null ? null : Deserialize(json);
    }

    public async Task SaveAsync(ChatRunState state, CancellationToken cancellationToken)
    {
        using var lease = await _writes.EnterAsync(cancellationToken);
        var document = new Document(SchemaVersion, state.ProjectId, state.ChatId, state.BranchId, state.Status,
            state.StreamingContent, state.Error, state.FailureKind, state.HasUnreadResponse, state.Revision,
            state.Queue.ToArray(), state.Operations.ToArray());
        var temp = paths.GetTemporaryPath(state.ProjectId, state.ChatId, state.BranchId);
        await fileSystem.WriteTextAsync(temp, JsonSerializer.Serialize(document, Options), cancellationToken);
        await fileSystem.MoveAsync(temp, paths.GetPath(state.ProjectId, state.ChatId, state.BranchId), true, cancellationToken);
    }

    public async Task<IReadOnlyList<ChatRunState>> ListAsync(CancellationToken cancellationToken)
    {
        var files = await fileSystem.ListFilesRecursivelyAsync(paths.RootDirectory, "*.run.json", cancellationToken);
        var result = new List<ChatRunState>();
        foreach (var file in files)
        {
            var json = await fileSystem.ReadTextAsync(file, cancellationToken);
            var state = json is null ? null : Deserialize(json);
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
                var state = json is null ? null : Deserialize(json);
                if (state is not null && retainedBranchIds.Contains(state.BranchId)) continue;
            }
            await fileSystem.DeleteFileAsync(file, cancellationToken);
        }
    }

    private static ChatRunState Deserialize(string json)
    {
        var document = JsonSerializer.Deserialize<Document>(json, Options);
        if (document is not { SchemaVersion: SchemaVersion } || document.ProjectId == Guid.Empty
            || document.ChatId == Guid.Empty || document.BranchId == Guid.Empty || document.Queue is null
            || document.Operations is null || !Enum.IsDefined(document.Status) || !Enum.IsDefined(document.FailureKind)
            || document.Queue.Any(item => !Enum.IsDefined(item.ParentMode) || !Enum.IsDefined(item.Stage)))
            throw new JsonException("Unsupported or invalid run document.");
        return Restore(document);
    }

    private static ChatRunState Restore(Document document) => ChatRunState.Restore(
        document.ProjectId,
        document.ChatId,
        document.BranchId,
        document.Status,
        document.StreamingContent,
        document.Error,
        document.FailureKind,
        document.HasUnreadResponse,
        document.Revision,
        document.Queue ?? Enumerable.Empty<QueuedRunMessage>(),
        document.Operations ?? Enumerable.Empty<Guid>());

    private sealed record Document(
        // ReSharper disable once MemberHidesStaticFromOuterClass
        int SchemaVersion,
        Guid ProjectId,
        Guid ChatId,
        Guid BranchId,
        RunStatus Status,
        string StreamingContent,
        string? Error,
        RunFailureKind FailureKind,
        bool HasUnreadResponse,
        long Revision,
        QueuedRunMessage[]? Queue,
        Guid[]? Operations);
}
