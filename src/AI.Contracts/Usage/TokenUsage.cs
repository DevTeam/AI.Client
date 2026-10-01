namespace AI.Contracts.Usage;

/// <summary>
/// Why a request reached the model. A turn's answer is only part of what it costs: routing, risk
/// assessment, compaction and subtasks are requests too, and without this a turn that looked short
/// could not say where its tokens went.
/// </summary>
public enum TokenUsagePurpose
{
    /// <summary>A step of the run answering the person.</summary>
    Answer,
    /// <summary>A step of a delegated subtask; counted in the turn that started it.</summary>
    Subtask,
    /// <summary>A summary written so older tool output can leave the context.</summary>
    Checkpoint,
    /// <summary>A summary written because the history no longer fit the window.</summary>
    Compaction,
    /// <summary>Choosing which skill fits the new message.</summary>
    Routing,
    /// <summary>Judging whether a tool call may run without asking.</summary>
    ToolRisk,
    /// <summary>Naming the chat.</summary>
    Title,
    /// <summary>Drafting the replies offered under an answer.</summary>
    ReplySuggestion,
    /// <summary>Any other skill that calls the model.</summary>
    Skill,
    /// <summary>A request sent straight to the completion endpoint, outside any run.</summary>
    Direct
}

/// <summary>
/// Token counts in the provider's terms. <paramref name="CachedInputTokens"/> is the part of
/// <paramref name="InputTokens"/> served from the provider's prompt cache, and
/// <paramref name="ReasoningTokens"/> the part of <paramref name="OutputTokens"/> the model spent
/// thinking — both are subsets, never added on top.
/// </summary>
public sealed record TokenCounts(long InputTokens, long OutputTokens, long CachedInputTokens = 0, long ReasoningTokens = 0);

/// <summary>What one model connection charges per million tokens, in the person's own currency.</summary>
/// <param name="CachedInput">The price of cached input; null charges it as ordinary input.</param>
public sealed record TokenPrices(decimal Input, decimal Output, decimal? CachedInput = null);

/// <summary>What changed at the start of a request since the previous one, and so cost its cached prefix.</summary>
public enum PromptPrefixChange
{
    /// <summary>The tool list: it precedes every message, so all of the request was new.</summary>
    Tools,
    /// <summary>An instruction leading the request.</summary>
    Instructions,
    /// <summary>A message of the conversation: a compaction, a summary, a trimmed tool result.</summary>
    History
}

/// <summary>
/// How a request started compared with the previous request of the same branch and purpose. A
/// provider can serve from its cache only the part both share, so this says what the cache could
/// have given, whatever it did give.
/// </summary>
/// <param name="ReusableTokens">Estimated tokens of the shared start: the tools and the messages both begin with.</param>
/// <param name="Change">What broke the shared start; null when the request carried on the previous one.</param>
public sealed record PromptPrefix(long ReusableTokens, PromptPrefixChange? Change);

/// <summary>
/// One request to a model, as it was measured.
/// </summary>
/// <param name="Estimated">
/// True when the endpoint did not report usage and the counts are this application's own estimate
/// of what was sent and received.
/// </param>
/// <param name="TurnId">The user message whose turn the request served, when it served one.</param>
/// <param name="FirstTokenMs">How long the stream took to produce its first chunk; null for a non-streamed request.</param>
/// <param name="Cost">
/// What the endpoint said the request cost, or what the connection's prices make it, or — when
/// neither is there — what the endpoint's own quotes for earlier requests make it; null when none
/// of these is known.
/// </param>
/// <param name="Prefix">How the request started compared with the one before it; null for the first.</param>
/// <param name="CostEstimated">True when <paramref name="Cost"/> was worked out from earlier quotes, not quoted or priced.</param>
public sealed record TokenUsageRecord(
    Guid Id,
    DateTimeOffset At,
    TokenUsagePurpose Purpose,
    string Model,
    Guid? ConnectionId,
    Guid? ProjectId,
    Guid? ChatId,
    Guid? BranchId,
    Guid? TurnId,
    TokenCounts Tokens,
    bool Estimated,
    long DurationMs,
    long? FirstTokenMs = null,
    decimal? Cost = null,
    PromptPrefix? Prefix = null,
    bool CostEstimated = false);

/// <summary>
/// Requests added together.
/// </summary>
/// <param name="EstimatedRequests">How many of <paramref name="Requests"/> carry estimated counts.</param>
/// <param name="Cost">The sum over priced requests; null when none was priced.</param>
/// <param name="PricedRequests">How many requests <paramref name="Cost"/> covers, so a partial sum is visible as one.</param>
/// <param name="EstimatedCostRequests">How many of the priced requests carry a cost worked out from earlier quotes.</param>
/// <param name="ReusableInputTokens">What the cache could have served: the shared start of each request compared.</param>
/// <param name="ToolChanges">Requests whose tool list differed from the previous request's.</param>
/// <param name="InstructionChanges">Requests whose leading instructions differed.</param>
/// <param name="HistoryChanges">Requests whose earlier conversation differed: compacted, summarized or trimmed.</param>
public sealed record TokenUsageTotals(
    TokenCounts Tokens,
    int Requests,
    int EstimatedRequests,
    decimal? Cost,
    int PricedRequests,
    long DurationMs,
    int EstimatedCostRequests = 0,
    long ReusableInputTokens = 0,
    int ToolChanges = 0,
    int InstructionChanges = 0,
    int HistoryChanges = 0);

/// <summary>Totals of one group: a purpose, a model or a day, as <paramref name="Key"/> names it.</summary>
public sealed record TokenUsageSlice(string Key, TokenUsageTotals Totals);

/// <summary>What one turn used, in total and by why the model was asked.</summary>
public sealed record TurnTokenUsage(Guid TurnId, Guid BranchId, DateTimeOffset StartedAt, TokenUsageTotals Totals,
    IReadOnlyList<TokenUsageSlice> ByPurpose);

/// <summary>
/// Everything one chat has used. Requests that belong to no turn — naming the chat, drafting reply
/// suggestions — are in <paramref name="Totals"/> and <paramref name="ByPurpose"/> but in no turn.
/// </summary>
/// <param name="RateLimits">What the endpoint the chat last used said is left of its limits, when it said.</param>
public sealed record ChatTokenUsage(Guid ProjectId, Guid ChatId, TokenUsageTotals Totals,
    IReadOnlyList<TokenUsageSlice> ByPurpose, IReadOnlyList<TurnTokenUsage> Turns, RateLimitStatus? RateLimits = null);

/// <summary>Usage over a period, grouped the ways a person asks about it.</summary>
/// <param name="ByDay">Keyed by UTC date, <c>yyyy-MM-dd</c>.</param>
/// <param name="ByModel">Keyed by model name as the endpoint reported it.</param>
public sealed record TokenUsageReport(DateTimeOffset From, DateTimeOffset To, TokenUsageTotals Totals,
    IReadOnlyList<TokenUsageSlice> ByDay, IReadOnlyList<TokenUsageSlice> ByModel,
    IReadOnlyList<TokenUsageSlice> ByPurpose);
