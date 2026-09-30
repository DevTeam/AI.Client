namespace AI.Application.Chat;

using AI.Contracts.Chats;

/// <summary>The context checkpoints of one chat, oldest first.</summary>
public interface IHistoryCheckpointRepository
{
    Task<IReadOnlyList<HistoryCheckpoint>> ListAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);

    /// <summary>Changes the chat's checkpoints under the repository's lock and returns what was saved.</summary>
    Task<IReadOnlyList<HistoryCheckpoint>> UpdateAsync(Guid projectId, Guid chatId,
        Func<IReadOnlyList<HistoryCheckpoint>, IReadOnlyList<HistoryCheckpoint>> change, CancellationToken cancellationToken);
}

/// <summary>
/// Summaries that stand in for a chat's earlier history in the requests after them. A request is
/// built from the full branch as always; the deepest checkpoint on that branch then replaces
/// everything up to the turn after it. Nothing stored or shown changes, so a checkpoint can be
/// removed at any time and the next request goes back to the full history.
/// </summary>
public interface IHistoryCheckpointService
{
    Task<IReadOnlyList<HistoryCheckpoint>> ListAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);

    /// <summary>The checkpoint that applies to <paramref name="context"/>: the deepest one whose covered message is in it.</summary>
    HistoryCheckpoint? Find(IReadOnlyList<HistoryCheckpoint> checkpoints, IReadOnlyList<ChatCompletionMessage> context);

    /// <summary>
    /// <paramref name="context"/> with everything up to the checkpoint replaced by its summary. The
    /// cut is made at the first user message after the covered one, so a tool exchange repaired
    /// after that message never loses the call it answers; without such a message the context is
    /// returned as it is.
    /// </summary>
    IReadOnlyList<ChatCompletionMessage> Apply(IReadOnlyList<ChatCompletionMessage> context, HistoryCheckpoint checkpoint);

    Task<IReadOnlyList<ChatCompletionMessage>> ApplyAsync(Guid projectId, Guid chatId,
        IReadOnlyList<ChatCompletionMessage> context, CancellationToken cancellationToken);

    Task<HistoryCheckpoint> AddAsync(Guid projectId, Guid chatId, HistoryCheckpoint checkpoint, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid projectId, Guid chatId, Guid checkpointId, CancellationToken cancellationToken);

    /// <summary>
    /// The turns of <paramref name="context"/> that a new checkpoint may cover: all but the last
    /// <paramref name="keepTurns"/>. Empty when nothing new would be covered — only an earlier
    /// summary and the kept turns are left.
    /// </summary>
    IReadOnlyList<ChatCompletionMessage> Coverable(IReadOnlyList<ChatCompletionMessage> context, int keepTurns);

    /// <summary>The message a checkpoint puts in place of the history it covers.</summary>
    ChatCompletionMessage SummaryMessage(HistoryCheckpoint checkpoint);
}

public sealed class HistoryCheckpointService(IHistoryCheckpointRepository repository) : IHistoryCheckpointService
{
    /// <summary>
    /// Kept per chat. A newer checkpoint on a branch already covers what an older one did, so the
    /// older ones matter only to branches forked before the newer ones; a few are plenty.
    /// </summary>
    private const int MaxCheckpointsPerChat = 32;

    /// <summary>How every stored or request-only history summary opens, so one can be told from a person's message.</summary>
    public const string SummaryPrefix = "Earlier conversation summary (LLM-generated; the detailed messages are omitted):\n";

    public Task<IReadOnlyList<HistoryCheckpoint>> ListAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken) =>
        repository.ListAsync(projectId, chatId, cancellationToken);

    public HistoryCheckpoint? Find(IReadOnlyList<HistoryCheckpoint> checkpoints, IReadOnlyList<ChatCompletionMessage> context)
    {
        if (checkpoints.Count == 0) return null;
        var positions = new Dictionary<Guid, int>();
        for (var index = 0; index < context.Count; index++)
            if (context[index].MessageId is { } id) positions.TryAdd(id, index);
        return checkpoints
            .Where(checkpoint => positions.ContainsKey(checkpoint.UpToMessageId))
            .OrderByDescending(checkpoint => positions[checkpoint.UpToMessageId])
            .ThenByDescending(checkpoint => checkpoint.CreatedAt)
            .FirstOrDefault();
    }

    public IReadOnlyList<ChatCompletionMessage> Apply(IReadOnlyList<ChatCompletionMessage> context, HistoryCheckpoint checkpoint)
    {
        var covered = -1;
        for (var index = context.Count - 1; index >= 0; index--)
            if (context[index].MessageId == checkpoint.UpToMessageId)
            {
                covered = index;
                break;
            }
        if (covered < 0) return context;
        var cut = -1;
        for (var index = covered + 1; index < context.Count; index++)
            if (context[index].Role == "user")
            {
                cut = index;
                break;
            }
        if (cut < 0) return context;
        // Messages the request carries ahead of the history — instructions — stay where they are.
        var lead = 0;
        while (lead < covered && context[lead].Role == "system") lead++;
        return context.Take(lead).Append(SummaryMessage(checkpoint)).Concat(context.Skip(cut)).ToArray();
    }

    public async Task<IReadOnlyList<ChatCompletionMessage>> ApplyAsync(Guid projectId, Guid chatId,
        IReadOnlyList<ChatCompletionMessage> context, CancellationToken cancellationToken)
    {
        var checkpoints = await repository.ListAsync(projectId, chatId, cancellationToken);
        return Find(checkpoints, context) is { } checkpoint ? Apply(context, checkpoint) : context;
    }

    public async Task<HistoryCheckpoint> AddAsync(Guid projectId, Guid chatId, HistoryCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        await repository.UpdateAsync(projectId, chatId, existing => existing
            .Where(item => item.Id != checkpoint.Id)
            .Append(checkpoint)
            .TakeLast(MaxCheckpointsPerChat)
            .ToArray(), cancellationToken);
        return checkpoint;
    }

    public async Task<bool> DeleteAsync(Guid projectId, Guid chatId, Guid checkpointId, CancellationToken cancellationToken)
    {
        var found = false;
        await repository.UpdateAsync(projectId, chatId, existing =>
        {
            found = existing.Any(item => item.Id == checkpointId);
            return existing.Where(item => item.Id != checkpointId).ToArray();
        }, cancellationToken);
        return found;
    }

    public IReadOnlyList<ChatCompletionMessage> Coverable(IReadOnlyList<ChatCompletionMessage> context, int keepTurns)
    {
        var starts = new List<int>();
        for (var index = 0; index < context.Count; index++)
            if (context[index].Role == "user") starts.Add(index);
        var turns = starts.Count - Math.Max(1, keepTurns);
        if (turns <= 0) return [];
        var covered = context.Skip(starts[0]).Take(starts[turns] - starts[0]).ToArray();
        // A lone earlier summary is not new history: covering only it would summarize a summary.
        var fresh = covered.Where(message => !IsSummary(message)).ToArray();
        return fresh.Any(message => message.MessageId is not null) ? covered : [];
    }

    public ChatCompletionMessage SummaryMessage(HistoryCheckpoint checkpoint) =>
        new("user", SummaryPrefix + checkpoint.Summary, MessageId: checkpoint.UpToMessageId);

    private static bool IsSummary(ChatCompletionMessage message) =>
        message.Role == "user" && message.Content.StartsWith(SummaryPrefix, StringComparison.Ordinal);
}
