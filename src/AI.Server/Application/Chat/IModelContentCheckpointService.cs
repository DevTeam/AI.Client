namespace AI.Application.Chat;

using AI.Contracts.Chats;
using Tools;

/// <summary>What a compaction covers.</summary>
public enum ContextCompactionScope
{
    /// <summary>The completed work of the current turn, for the rest of this run only.</summary>
    Turn,
    /// <summary>
    /// The chat's earlier turns, all but the current one and the one before it. Kept as a history
    /// checkpoint, so later turns start from the summary too.
    /// </summary>
    History
}

public interface IModelContentCheckpointService
{
    /// <param name="model">The model the summaries are written by, recorded on a kept checkpoint.</param>
    IDisposable Begin(ToolRunContext run, string model, Func<string, CancellationToken, Task<string>> summarize);
    void Update(ToolRunContext run, IReadOnlyList<ChatCompletionMessage> context);
    IReadOnlyList<ChatCompletionMessage> Apply(ToolRunContext run, IReadOnlyList<ChatCompletionMessage> context);

    /// <summary>
    /// Makes <paramref name="checkpoint"/> stand in for the history it covers for the rest of the
    /// run. The run's own context was built before the checkpoint existed, so without this each
    /// later step would carry the full history again.
    /// </summary>
    void PinHistory(ToolRunContext run, HistoryCheckpoint checkpoint);

    ModelContentCompactionPreview Preview(ToolRunContext run, ContextCompactionScope scope = ContextCompactionScope.Turn);
    Task<ModelContentCompactionResult> CompactAsync(ToolRunContext run, int targetTokens, ContextCompactionScope scope,
        CancellationToken cancellationToken);

    /// <summary>Drops the turn checkpoint, or for the history the checkpoint the run now applies.</summary>
    Task<bool> ResetAsync(ToolRunContext run, ContextCompactionScope scope, CancellationToken cancellationToken);
}

public sealed record ModelContentCompactionPreview(int CoveredMessages, long SourceCharacters, bool CanCompact);

public sealed record ModelContentCompactionResult(
    int CoveredMessages,
    long SourceCharacters,
    int SummaryCharacters,
    bool Applied,
    string Guidance);
