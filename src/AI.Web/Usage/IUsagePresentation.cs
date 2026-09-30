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
    /// Input and output as "42k → 1.8k". A leading "≈" says some of it is the application's own
    /// estimate, because the endpoint did not report what it used.
    /// </summary>
    string FormatFlow(TokenUsageTotals totals);

    /// <summary>The whole tokens as a grouped number, "42 180", for places with room for it.</summary>
    string FormatExact(long tokens);

    bool IsEstimated(TokenUsageTotals totals);

    /// <summary>The percentage of input served from the provider's cache; null when there was no input.</summary>
    int? CachePercent(TokenCounts tokens);

    /// <summary>The cost, or null when nothing was priced. Partial sums are marked as such by the caller.</summary>
    string? FormatCost(TokenUsageTotals totals);

    /// <summary>Output tokens per second of request time; null when too little was measured to say.</summary>
    int? OutputSpeed(TokenUsageTotals totals);

    string PurposeLabel(string key);

    /// <summary>The purposes, largest first; a purpose that used nothing is left out.</summary>
    IReadOnlyList<UsageShare> Shares(IReadOnlyList<TokenUsageSlice> slices);
}
