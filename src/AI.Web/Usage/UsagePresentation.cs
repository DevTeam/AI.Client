namespace AI.Web.Usage;

using System.Globalization;
using AI.Contracts.Usage;
using AI.Web.Composer;

public sealed class UsagePresentation(IComposerContextPresentation context) : IUsagePresentation
{
    /// <summary>
    /// Below this much measured time a speed says more about the clock than the model: a cached,
    /// one-word answer would read as thousands of tokens a second.
    /// </summary>
    private const long MinSpeedDurationMs = 500;

    public string FormatTokens(long tokens) => context.FormatTokens(tokens);

    public string FormatFlow(TokenUsageTotals totals) =>
        (IsEstimated(totals) ? "≈" : string.Empty)
        + $"{FormatTokens(totals.Tokens.InputTokens)} → {FormatTokens(totals.Tokens.OutputTokens)}";

    public string FormatExact(long tokens)
    {
        var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        format.NumberGroupSeparator = " ";
        return tokens.ToString("#,0", format);
    }

    public bool IsEstimated(TokenUsageTotals totals) => totals.EstimatedRequests > 0;

    public int? CachePercent(TokenCounts tokens) => tokens.InputTokens <= 0
        ? null
        : (int)Math.Round(100d * Math.Min(tokens.CachedInputTokens, tokens.InputTokens) / tokens.InputTokens);

    public string? FormatCost(TokenUsageTotals totals) => totals.Cost switch
    {
        null => null,
        0m => "$0",
        < 0.01m => "<$0.01",
        { } cost => "$" + cost.ToString(cost < 10m ? "0.00" : "0", CultureInfo.InvariantCulture)
    };

    public int? OutputSpeed(TokenUsageTotals totals) =>
        totals.DurationMs < MinSpeedDurationMs || totals.Tokens.OutputTokens <= 0
            ? null
            : (int)Math.Round(totals.Tokens.OutputTokens * 1000d / totals.DurationMs);

    public string PurposeLabel(string key) => key switch
    {
        nameof(TokenUsagePurpose.Answer) => "Answer",
        nameof(TokenUsagePurpose.Subtask) => "Subtasks",
        nameof(TokenUsagePurpose.Checkpoint) => "Tool result summaries",
        nameof(TokenUsagePurpose.Compaction) => "Compaction",
        nameof(TokenUsagePurpose.Routing) => "Skill routing",
        nameof(TokenUsagePurpose.ToolRisk) => "Approval checks",
        nameof(TokenUsagePurpose.Title) => "Chat title",
        nameof(TokenUsagePurpose.ReplySuggestion) => "Reply suggestions",
        nameof(TokenUsagePurpose.Skill) => "Skills",
        nameof(TokenUsagePurpose.Direct) => "Direct requests",
        _ => key
    };

    public IReadOnlyList<UsageShare> Shares(IReadOnlyList<TokenUsageSlice> slices)
    {
        var total = slices.Sum(Size);
        if (total <= 0) return [];
        return slices.Where(slice => Size(slice) > 0)
            .Select(slice => new UsageShare(slice.Key, PurposeLabel(slice.Key), Size(slice), (double)Size(slice) / total))
            .OrderByDescending(share => share.Tokens)
            .ToArray();
    }

    private static long Size(TokenUsageSlice slice) => slice.Totals.Tokens.InputTokens + slice.Totals.Tokens.OutputTokens;
}
