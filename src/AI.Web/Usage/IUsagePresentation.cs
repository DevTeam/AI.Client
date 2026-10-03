namespace AI.Web.Usage;

using AI.Contracts.Usage;

/// <summary>One purpose's part of the tokens a chat or turn used.</summary>
/// <param name="Fraction">The part of all tokens, input and output together, from 0 to 1.</param>
public sealed record UsageShare(string Key, string Label, long Tokens, double Fraction);

/// <summary>How token usage reads on screen: short figures, honest about what was estimated.</summary>
public interface IUsagePresentation
{
    string FormatTokens(long tokens);

    /// <summary>
    /// Cumulative input and output as "42k in → 1.8k out". A leading "≈" says some of it is the application's own
    /// estimate, because the endpoint did not report what it used.
    /// </summary>
    string FormatFlow(TokenUsageTotals totals);

    /// <summary>The whole tokens as a grouped number, "42 180", for places with room for it.</summary>
    string FormatExact(long tokens);

    bool IsEstimated(TokenUsageTotals totals);

    /// <summary>The percentage of input served from the provider's cache; null when there was no input.</summary>
    int? CachePercent(TokenCounts tokens);

    /// <summary>
    /// The cost, or null when nothing was priced. A leading "≈" says part of it was worked out from
    /// the endpoint's quotes for other requests. Partial sums are marked as such by the caller.
    /// </summary>
    string? FormatCost(TokenUsageTotals totals);

    /// <summary>
    /// Estimated overlap divided by estimated full input of compared requests. Both counts use
    /// the same scale; this is not provider cache eligibility. Null without a recorded denominator.
    /// </summary>
    int? ReusablePercent(TokenUsageTotals totals);

    /// <summary>What broke the shared start of requests, "tools 2 · history 1"; null when nothing did.</summary>
    string? FormatPrefixChanges(TokenUsageTotals totals);

    /// <summary>
    /// The provider's limits, "498 of 500 requests · 39k tokens left · resets in 6 min"; null when
    /// the endpoint stated none or what it stated is too old to mean anything now.
    /// </summary>
    string? FormatRateLimits(RateLimitStatus? limits, DateTimeOffset now);

    /// <summary>True when either window is down to its last tenth.</summary>
    bool IsRateLimitLow(RateLimitStatus? limits);

    /// <summary>Output tokens per second of request time; null when too little was measured to say.</summary>
    int? OutputSpeed(TokenUsageTotals totals);

    string PurposeLabel(string key);

    /// <summary>The purposes, largest first; a purpose that used nothing is left out.</summary>
    IReadOnlyList<UsageShare> Shares(IReadOnlyList<TokenUsageSlice> slices);
}
