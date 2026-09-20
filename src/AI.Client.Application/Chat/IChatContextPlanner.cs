namespace AI.Client.Application.Chat;

using Contracts.Chat;
using Contracts.Settings;

public interface IChatContextPlanner
{
    ContextPlan Plan(
        ConnectionSettings? connection,
        string model,
        IReadOnlyList<ChatCompletionMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools);

    /// <summary>
    /// Same as <see cref="Plan"/> but, when deterministic compaction still does not fit the
    /// request, falls back to an isolated, tool-free LLM summary. Pass a null summarizer to
    /// reproduce the synchronous behaviour for callers that cannot await a network call.
    /// </summary>
    Task<ContextPlan> PlanAsync(
        ConnectionSettings? connection,
        string model,
        IReadOnlyList<ChatCompletionMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools,
        IContextSummarizer? summarizer,
        int summaryTargetTokens,
        CancellationToken cancellationToken);
}
