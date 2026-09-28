namespace AI.Contracts.Runs;

using Settings;

/// <summary>
/// How the model's context window was divided by the last request of a branch, layer by layer.
/// </summary>
/// <remarks>
/// It is the measured request, not the stored history: tool results are counted as the model
/// sees them and compacted turns as their summary. The answer that ended the run is added to
/// <paramref name="HistoryTokens"/> so the figure describes what the next request will start from.
/// The reserved layers are space held back from the input, not tokens already sent.
/// </remarks>
public sealed record ContextUsage(
    long ContextWindowTokens,
    ContextLimitSource ContextWindowSource,
    long InstructionTokens,
    long ToolTokens,
    long HistoryTokens,
    long ReservedOutputTokens,
    long OverheadTokens,
    bool WasCompacted,
    int OmittedMessages);
