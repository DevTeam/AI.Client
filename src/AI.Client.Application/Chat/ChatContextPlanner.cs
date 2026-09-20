namespace AI.Client.Application.Chat;

using Contracts.Chat;

/// <summary>
/// Measures a request against conservative OpenAI-compatible defaults. Model-specific and
/// connection-level overrides can be added without changing the agent or transport boundary.
/// </summary>
public sealed class ChatContextPlanner(
    IContextTokenEstimator estimator,
    IChatContextCompactor compactor) : IChatContextPlanner
{
    private readonly ChatContextLimits _unknownModelLimits = new(
        ContextWindowTokens: 32_768,
        ReservedOutputTokens: 4_096,
        ProtocolOverheadTokens: 256,
        SafetyMarginTokens: 1_024);

    public ContextPlan Plan(
        string model,
        IReadOnlyList<ChatCompletionMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(tools);

        var limits = ResolveLimits(model);
        var toolTokens = estimator.EstimateTools(tools);
        var fixedCost = Add(limits.ReservedOutputTokens, toolTokens,
            limits.ProtocolOverheadTokens, limits.SafetyMarginTokens);
        var inputLimit = Math.Max(0, limits.ContextWindowTokens - Math.Min(limits.ContextWindowTokens, fixedCost));
        var estimated = estimator.EstimateMessages(messages);
        var compaction = estimated > inputLimit
            ? compactor.Compact(messages, inputLimit)
            : new ContextCompactionResult(messages, 0, false);
        return new ContextPlan(
            inputLimit,
            estimator.EstimateMessages(compaction.Messages),
            limits.ReservedOutputTokens,
            toolTokens,
            compaction.WasCompacted,
            compaction.OmittedMessages,
            compaction.Messages);
    }

    private ChatContextLimits ResolveLimits(string model)
    {
        _ = model;
        return _unknownModelLimits;
    }

    private static long Add(params long[] values)
    {
        long total = 0;
        foreach (var value in values)
            total = total > long.MaxValue - value ? long.MaxValue : total + value;
        return total;
    }
}
