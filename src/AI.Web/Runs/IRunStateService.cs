namespace AI.Web.Runs;

using AI.Contracts.Runs;

/// <summary>
/// Holds the run snapshots the client is showing, keyed by chat and branch, and applies the
/// incremental updates the Host streams.
/// </summary>
public interface IRunStateService
{
    IReadOnlyDictionary<RunKey, ChatRunSnapshot> Runs { get; }

    void RemoveMissing(IReadOnlyList<ChatRunSnapshot> snapshot);

    void Remove(IReadOnlyList<ChatRunKey> keys);

    void AppendStreaming(IReadOnlyList<ChatRunStreamingAppend> appends);

    /// <summary>Extends the draft of each run whose draft is the one the append was made from.</summary>
    void AppendDraft(IReadOnlyList<ChatRunDraftAppend> appends);

    void Store(ChatRunSnapshot run);

    /// <summary>Elapsed time spent with the model itself generating, excluding tool and approval waits.</summary>
    TimeSpan? GetLlmGeneratingElapsed(RunKey key);
}
