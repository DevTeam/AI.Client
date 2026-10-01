namespace AI.Application.Chat;

using Contracts.Settings;

public interface IChatContextPlanner
{
    ContextPlan Plan(
        ConnectionSettings? connection,
        string model,
        IReadOnlyList<ChatCompletionMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools,
        string? trailing = null,
        ContextCompactionMemory? memory = null);

    /// <summary>
    /// Same as <see cref="Plan"/> but, when deterministic compaction still does not fit the
    /// request, falls back to an isolated, tool-free LLM summary. Pass a null summarizer to
    /// reproduce the synchronous behaviour for callers that cannot await a network call.
    /// </summary>
    /// <param name="trailing">
    /// Guidance attached to the end of the last message — or after it, when that is the model's own
    /// — counted, never compacted.
    /// </param>
    /// <param name="memory">
    /// The run's previous compaction. With it a compaction is carried on while the request still
    /// fits, rather than redone, so the request keeps its cached prefix.
    /// </param>
    Task<ContextPlan> PlanAsync(
        ConnectionSettings? connection,
        string model,
        IReadOnlyList<ChatCompletionMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools,
        IContextSummarizer? summarizer,
        int summaryTargetTokens,
        CancellationToken cancellationToken,
        string? trailing = null,
        ContextCompactionMemory? memory = null);
}
