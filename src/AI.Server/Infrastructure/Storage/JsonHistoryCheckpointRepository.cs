namespace AI.Infrastructure.Storage;

using System.Text.Json;
using System.Text.Json.Serialization;
using AI.Application.Chat;
using AI.Contracts.Chats;
using AI.Domain.Chats;
using AI.Domain.Projects;

/// <summary>
/// A chat's context checkpoints in <c>{chat}.context.json</c> beside the chat itself. Apart from
/// the chat document on purpose: they are the model's view of the history, not the history, and
/// losing the file only means the next request is built from the full branch again.
/// </summary>
public sealed class JsonHistoryCheckpointRepository(IChatStoragePaths paths, ITextFileSystem files)
    : IHistoryCheckpointRepository, IDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AsyncGate _gate = new();

    public void Dispose() => _gate.Dispose();

    public async Task<IReadOnlyList<HistoryCheckpoint>> ListAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken);
        return await LoadAsync(projectId, chatId, cancellationToken);
    }

    public async Task<IReadOnlyList<HistoryCheckpoint>> UpdateAsync(Guid projectId, Guid chatId,
        Func<IReadOnlyList<HistoryCheckpoint>, IReadOnlyList<HistoryCheckpoint>> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        using var lease = await _gate.EnterAsync(cancellationToken);
        var next = change(await LoadAsync(projectId, chatId, cancellationToken)).ToArray();
        var path = Path(projectId, chatId);
        if (next.Length == 0)
        {
            await files.DeleteAsync(path, cancellationToken);
            return next;
        }

        var temporary = path + ".tmp";
        await files.WriteTextAsync(temporary, JsonSerializer.Serialize(next, Json), cancellationToken);
        await files.MoveAsync(temporary, path, true, cancellationToken);
        return next;
    }

    private async Task<IReadOnlyList<HistoryCheckpoint>> LoadAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        var text = await files.ReadTextAsync(Path(projectId, chatId), cancellationToken);
        if (string.IsNullOrWhiteSpace(text)) return [];
        try
        {
            return JsonSerializer.Deserialize<HistoryCheckpoint[]>(text, Json) ?? [];
        }
        catch (JsonException)
        {
            // A damaged file costs the summaries, never the chat: requests go back to the full history.
            return [];
        }
    }

    private string Path(Guid projectId, Guid chatId) =>
        paths.GetHistoryCheckpointsPath(new ChatId(chatId), new ProjectId(projectId));
}
