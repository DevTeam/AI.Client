namespace AI.Client.Application.Chat;

using Contracts.Chat;
using Contracts.Settings;

/// <summary>
/// Measures a request against conservative OpenAI-compatible defaults. Model-specific and
/// connection-level overrides can be added without changing the agent or transport boundary.
/// </summary>
public sealed class ChatContextPlanner(
    IContextTokenEstimator estimator,
    IChatContextCompactor compactor,
    IConnectionContextLimitsResolver limitsResolver) : IChatContextPlanner
{
    public ContextPlan Plan(
        ConnectionSettings? connection,
        string model,
        IReadOnlyList<ChatCompletionMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(tools);

        var (effective, inputLimit, toolTokens, compaction) = PlanCore(connection, messages, tools);
        return BuildPlan(effective, inputLimit, toolTokens, compaction);
    }

    public async Task<ContextPlan> PlanAsync(
        ConnectionSettings? connection,
        string model,
        IReadOnlyList<ChatCompletionMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools,
        IContextSummarizer? summarizer,
        int summaryTargetTokens,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(tools);

        var (effective, inputLimit, toolTokens, compaction) = PlanCore(connection, messages, tools);
        if (estimator.EstimateMessages(compaction.Messages) > inputLimit && summarizer is not null)
        {
            compaction = await compactor.CompactWithLlmAsync(messages, inputLimit, summaryTargetTokens,
                summarizer, cancellationToken);
        }

        return BuildPlan(effective, inputLimit, toolTokens, compaction);
    }

    private (ResolvedConnectionContextLimits Effective, long InputLimit, long ToolTokens,
        ContextCompactionResult Compaction) PlanCore(
        ConnectionSettings? connection,
        IReadOnlyList<ChatCompletionMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools)
    {
        var effective = limitsResolver.Resolve(connection);
        var limits = new ChatContextLimits(
            effective.ContextWindowTokens,
            effective.ReservedOutputTokens,
            ProtocolOverheadTokens: 256,
            SafetyMarginTokens: 1_024);
        var toolTokens = estimator.EstimateTools(tools);
        var fixedCost = Add(limits.ReservedOutputTokens, toolTokens,
            limits.ProtocolOverheadTokens, limits.SafetyMarginTokens);
        var inputLimit = Math.Max(0, limits.ContextWindowTokens - Math.Min(limits.ContextWindowTokens, fixedCost));
        var estimated = estimator.EstimateMessages(messages);
        var compaction = estimated > inputLimit
            ? compactor.Compact(messages, inputLimit)
            : new ContextCompactionResult(messages, 0, false);
        return (effective, inputLimit, toolTokens, compaction);
    }

    private ContextPlan BuildPlan(ResolvedConnectionContextLimits effective, long inputLimit, long toolTokens,
        ContextCompactionResult compaction) =>
        new(
            inputLimit,
            estimator.EstimateMessages(compaction.Messages),
            effective.ReservedOutputTokens,
            toolTokens,
            effective.ContextWindowTokens,
            effective.ContextWindowSource,
            effective.ReservedOutputSource,
            compaction.WasCompacted,
            compaction.OmittedMessages,
            compaction.Messages);

    private static long Add(params long[] values)
    {
        long total = 0;
        foreach (var value in values)
            total = total > long.MaxValue - value ? long.MaxValue : total + value;
        return total;
    }
}
