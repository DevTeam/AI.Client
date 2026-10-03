namespace AI.Application.Chat;

using Contracts.Settings;

/// <summary>
/// Measures a request against conservative OpenAI-compatible defaults. Model-specific and
/// connection-level overrides can be added without changing the agent or transport boundary.
/// </summary>
public sealed class ChatContextPlanner(
    IContextTokenEstimator tokenEstimator,
    IChatContextCompactor compactor,
    IConnectionContextLimitsResolver limitsResolver, IAdaptiveContextPolicy policy) : IChatContextPlanner
{
    public ContextPlan Plan(
        ConnectionSettings? connection,
        string model,
        IReadOnlyList<ChatCompletionMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools,
        string? trailing = null,
        ContextCompactionMemory? memory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(tools);

        if (connection is not null) connection = connection with { Model = model };
        var core = PlanCore(connection, messages, tools, trailing, memory);
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
        string? trailing = null,
        ContextCompactionMemory? memory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(tools);

        if (connection is not null) connection = connection with { Model = model };
        var core = PlanCore(connection, messages, tools, trailing, memory);
        var estimator = core.Estimator;
        var compaction = core.Compaction;
        if (estimator.EstimateMessages(compaction.Messages) > core.ConversationLimit && summarizer is not null
            && estimator.EstimateMessages(messages.Where(message => message.Role == "system")
                .Concat(messages.Where(message => message.Role == "user" && !message.IsContextSummary).TakeLast(1)).ToArray()) <= core.ConversationLimit)
        {
            var candidate = await compactor.CompactWithLlmAsync(messages, core.TargetTokens,
                summaryTargetTokens, summarizer, cancellationToken, connection);
            if (policy.ShouldAcceptCompaction(compaction.Messages, candidate.Messages, 0, connection))
            {
                compaction = candidate;
                core = core with { Reason = "llm_fallback" };
                memory?.Remember(messages, compaction);
            }
        }

        return BuildPlan(core, compaction);
    }

    private PlanBasis PlanCore(
        ConnectionSettings? connection,
        IReadOnlyList<ChatCompletionMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools,
        string? trailing,
        ContextCompactionMemory? memory)
    {
        var estimator = tokenEstimator.ForModel(connection?.Model);
        var effective = limitsResolver.Resolve(connection);
        var budget = policy.Resolve(connection);
        var toolTokens = estimator.EstimateTools(tools);
        var compactionBudget = policy.ResolveCompaction(connection, toolTokens, TrailingTokens(trailing, estimator), memory is not null);
        var inputLimit = compactionBudget.InputLimit;
        var conversationLimit = compactionBudget.MessageLimit;
        ContextCompactionResult compaction;
        var reason = "none";
        if (estimator.EstimateMessages(messages) <= conversationLimit)
        {
            compaction = new ContextCompactionResult(messages, 0, false);
            memory?.Forget();
        }
        else if (memory?.Continue(messages) is { } continued
                 && estimator.EstimateMessages(continued.Messages) <= conversationLimit)
        {
            compaction = continued;
            reason = "reused_projection";
        }
        else
        {
            compaction = compactor.Compact(messages, compactionBudget.TargetTokens, connection?.Model);
            reason = "input_pressure";
            memory?.Remember(messages, compaction);
        }
        return new PlanBasis(effective, budget, inputLimit, conversationLimit, compactionBudget.TargetTokens,
            toolTokens, compaction, trailing, estimator, estimator.EstimateMessages(messages), reason);
    }

    private sealed record PlanBasis(ResolvedConnectionContextLimits Effective, AdaptiveContextBudget Budget,
        long InputLimit, long ConversationLimit, long TargetTokens, long ToolTokens, ContextCompactionResult Compaction,
        string? Trailing, IContextTokenEstimator Estimator, long OriginalTokens, string Reason);

    private static long TrailingTokens(string? trailing, IContextTokenEstimator estimator) =>
        trailing is null ? 0 : estimator.EstimateMessages([new ChatCompletionMessage("user", trailing)]);

    /// <summary>
    /// The guidance goes at the end of the last message, and the request keeps its alternation of
    /// roles. After the model's own words — an answer cut off and being continued — it follows as a
    /// message of its own, since added to them it would read as the model's.
    /// </summary>
    private static IReadOnlyList<ChatCompletionMessage> Attach(IReadOnlyList<ChatCompletionMessage> messages, string? trailing)
    {
        if (trailing is null) return messages;
        if (messages.Count == 0 || messages[^1].Role is "assistant" or "system")
            return [.. messages, new ChatCompletionMessage("user", trailing)];
        var attached = messages.ToArray();
        attached[^1] = attached[^1] with { ModelContent = attached[^1].ForModel + "\n\n" + trailing };
        return attached;
    }

    private static ContextPlan BuildPlan(PlanBasis basis, ContextCompactionResult compaction)
    {
        var estimator = basis.Estimator;
        var messages = Attach(compaction.Messages, basis.Trailing);
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
            + TrailingTokens(basis.Trailing, estimator),
            basis.Budget.OverheadTokens,
            compaction.Summary,
            Math.Max(0, basis.OriginalTokens - estimator.EstimateMessages(compaction.Messages)),
            basis.Reason);
    }

}
