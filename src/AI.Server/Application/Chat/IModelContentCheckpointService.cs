namespace AI.Application.Chat;

using AI.Contracts.Chats;
using Tools;

/// <summary>What a compaction covers.</summary>
public enum ContextCompactionScope
{
    /// <summary>The completed work of the current turn, for the rest of this run only.</summary>
    Turn,
    /// <summary>
    /// The chat's earlier turns: all but the current one, and the one before it when it is small
    /// enough to keep. Kept as a history checkpoint, so later turns start from the summary too.
    /// </summary>
    History
}

public interface IModelContentCheckpointService
{
    /// <param name="model">The model the summaries are written by, recorded on a kept checkpoint.</param>
    /// <param name="historyKeepTokens">How much of the recent history a history compaction leaves in full.</param>
    IDisposable Begin(ToolRunContext run, string model, long historyKeepTokens,
        Func<string, CancellationToken, Task<string>> summarize);
    void Update(ToolRunContext run, IReadOnlyList<ChatCompletionMessage> context);
    /// <summary>Uses the policy's current message allowance for the recent history kept in full.</summary>
    void UpdateBudget(ToolRunContext run, AdaptiveCompactionBudget budget);
    IReadOnlyList<ChatCompletionMessage> Apply(ToolRunContext run, IReadOnlyList<ChatCompletionMessage> context);

    /// <summary>
    /// Makes <paramref name="checkpoint"/> stand in for the history it covers for the rest of the
    /// run. The run's own context was built before the checkpoint existed, so without this each
    /// later step would carry the full history again.
    /// </summary>
    void PinHistory(ToolRunContext run, HistoryCheckpoint checkpoint);

    ModelContentCompactionPreview Preview(ToolRunContext run, ContextCompactionScope scope = ContextCompactionScope.Turn);
    /// <param name="origin">Who asked for a history compaction, recorded on the checkpoint it keeps.</param>
    Task<ModelContentCompactionResult> CompactAsync(ToolRunContext run, int targetTokens, ContextCompactionScope scope,
        CancellationToken cancellationToken, HistoryCheckpointOrigin origin = HistoryCheckpointOrigin.Model,
        long minimumGainTokens = 0);

    /// <summary>
    /// Summarizes the completed steps of the turn in progress, as a turn compaction does, with no
    /// <c>context_compact</c> call to mark where: the most recent steps that fit
    /// <paramref name="keepTokens"/> stay in full, at least the last one. An earlier summary of this
    /// turn is folded into the new one. Nothing happens when less than
    /// <paramref name="minimumTokens"/> would be covered.
    /// </summary>
    Task<ModelContentCompactionResult> CompactTurnAheadAsync(ToolRunContext run, long keepTokens, long minimumTokens,
        int targetTokens, CancellationToken cancellationToken);

    /// <summary>Drops the turn checkpoint, or for the history the checkpoint the run now applies.</summary>
    Task<bool> ResetAsync(ToolRunContext run, ContextCompactionScope scope, CancellationToken cancellationToken);
}

public sealed record ModelContentCompactionPreview(int CoveredMessages, long SourceCharacters, bool CanCompact,
    long SourceTokens = 0);

public sealed record ModelContentCompactionResult(
    int CoveredMessages,
    long SourceCharacters,
    int SummaryCharacters,
    bool Applied,
    string Guidance);
