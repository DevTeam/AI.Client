namespace AI.Application.Chat;

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
    /// <summary>
    /// When a compaction is needed and the run keeps a memory of it, the request is compacted to
    /// this share of the limit, leaving room for the steps that follow to append to it unchanged.
    /// </summary>
    private const int CompactionSlackPercent = 80;

    public ContextPlan Plan(
        ConnectionSettings? connection,
        string model,
        IReadOnlyList<ChatCompletionMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools,
        IReadOnlyList<ChatCompletionMessage>? trailing = null,
        ContextCompactionMemory? memory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(tools);

        var core = PlanCore(connection, messages, tools, trailing ?? [], memory);
        return BuildPlan(core, core.Compaction);
    }

    public async Task<ContextPlan> PlanAsync(
        ConnectionSettings? connection,
        string model,
        IReadOnlyList<ChatCompletionMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools,
        IContextSummarizer? summarizer,
        int summaryTargetTokens,
        CancellationToken cancellationToken,
        IReadOnlyList<ChatCompletionMessage>? trailing = null,
        ContextCompactionMemory? memory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(tools);

        var core = PlanCore(connection, messages, tools, trailing ?? [], memory);
        var compaction = core.Compaction;
        if (estimator.EstimateMessages(compaction.Messages) > core.ConversationLimit && summarizer is not null)
        {
            compaction = await compactor.CompactWithLlmAsync(messages, Target(core.ConversationLimit, memory),
                summaryTargetTokens, summarizer, cancellationToken);
            memory?.Remember(messages, compaction);
        }

        return BuildPlan(core, compaction);
    }

    private PlanBasis PlanCore(
        ConnectionSettings? connection,
        IReadOnlyList<ChatCompletionMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools,
        IReadOnlyList<ChatCompletionMessage> trailing,
        ContextCompactionMemory? memory)
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
        // The trailing note is never compacted: it is what this step is asked to do.
        var conversationLimit = Math.Max(0, inputLimit - (trailing.Count == 0 ? 0 : estimator.EstimateMessages(trailing)));
        ContextCompactionResult compaction;
        if (estimator.EstimateMessages(messages) <= conversationLimit)
        {
            compaction = new ContextCompactionResult(messages, 0, false);
            memory?.Forget();
        }
        else if (memory?.Continue(messages) is { } continued
                 && estimator.EstimateMessages(continued.Messages) <= conversationLimit)
        {
            compaction = continued;
        }
        else
        {
            compaction = compactor.Compact(messages, Target(conversationLimit, memory));
            memory?.Remember(messages, compaction);
        }
        return new PlanBasis(effective, limits, inputLimit, conversationLimit, toolTokens, compaction, trailing);
    }

    /// <summary>A run that remembers its compaction compacts below the limit, so later steps can reuse it.</summary>
    private static long Target(long limit, ContextCompactionMemory? memory) =>
        memory is null ? limit : limit / 100 * CompactionSlackPercent;

    private sealed record PlanBasis(ResolvedConnectionContextLimits Effective, ChatContextLimits Limits,
        long InputLimit, long ConversationLimit, long ToolTokens, ContextCompactionResult Compaction,
        IReadOnlyList<ChatCompletionMessage> Trailing);

    private ContextPlan BuildPlan(PlanBasis basis, ContextCompactionResult compaction)
    {
        var messages = basis.Trailing.Count == 0 ? compaction.Messages : compaction.Messages.Concat(basis.Trailing).ToArray();
        return new(
            basis.InputLimit,
            estimator.EstimateMessages(messages),
            basis.Effective.ReservedOutputTokens,
            basis.ToolTokens,
            basis.Effective.ContextWindowTokens,
            basis.Effective.ContextWindowSource,
            basis.Effective.ReservedOutputSource,
            compaction.WasCompacted,
            compaction.OmittedMessages,
            messages,
            estimator.EstimateMessages(compaction.Messages.Where(message => message.Role == "system").ToArray())
            + (basis.Trailing.Count == 0 ? 0 : estimator.EstimateMessages(basis.Trailing)),
            Add(basis.Limits.ProtocolOverheadTokens, basis.Limits.SafetyMarginTokens),
            compaction.Summary);
    }

    private static long Add(params long[] values)
    {
        long total = 0;
        foreach (var value in values)
            total = total > long.MaxValue - value ? long.MaxValue : total + value;
        return total;
    }
}
