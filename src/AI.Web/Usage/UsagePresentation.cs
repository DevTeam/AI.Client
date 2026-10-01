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

    /// <summary>Limits stated longer ago than this have most likely refilled and are not shown.</summary>
    private static readonly TimeSpan RateLimitsAge = TimeSpan.FromMinutes(30);

    public string? FormatCost(TokenUsageTotals totals) => totals.Cost switch
    {
        null => null,
        0m => "$0",
        < 0.01m => "<$0.01",
        { } cost => (totals.EstimatedCostRequests > 0 ? "≈" : string.Empty)
                    + "$" + cost.ToString(cost < 10m ? "0.00" : "0", CultureInfo.InvariantCulture)
    };

    public int? ReusablePercent(TokenUsageTotals totals) =>
        totals.ReusableInputTokens <= 0 || totals.Tokens.InputTokens <= 0
            ? null
            : (int)Math.Round(100d * Math.Min(totals.ReusableInputTokens, totals.Tokens.InputTokens) / totals.Tokens.InputTokens);

    public string? FormatPrefixChanges(TokenUsageTotals totals)
    {
        var parts = new List<string>();
        if (totals.ToolChanges > 0) parts.Add($"tools {totals.ToolChanges}");
        if (totals.InstructionChanges > 0) parts.Add($"instructions {totals.InstructionChanges}");
        if (totals.HistoryChanges > 0) parts.Add($"history {totals.HistoryChanges}");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    public string? FormatRateLimits(RateLimitStatus? limits, DateTimeOffset now)
    {
        if (limits is null || now - limits.ObservedAt > RateLimitsAge) return null;
        var parts = new List<string>();
        if (Window(limits.Requests, "requests", exact: true) is { } requests) parts.Add(requests);
        if (Window(limits.Tokens, "tokens", exact: false) is { } tokens) parts.Add(tokens);
        var reset = new[] { limits.Requests?.ResetsAt, limits.Tokens?.ResetsAt }
            .Where(at => at is not null && at > now).Min();
        if (reset is { } at && parts.Count > 0) parts.Add("resets in " + Duration(at - now));
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    public bool IsRateLimitLow(RateLimitStatus? limits) =>
        limits is not null && (Low(limits.Requests) || Low(limits.Tokens));

    private static bool Low(RateLimitWindow? window) =>
        window is { Limit: > 0 and var limit, Remaining: { } remaining } && remaining * 10 <= limit;

    private string? Window(RateLimitWindow? window, string unit, bool exact)
    {
        if (window?.Remaining is not { } remaining) return null;
        string Format(long value) => exact ? value.ToString(CultureInfo.InvariantCulture) : FormatTokens(value);
        return window.Limit is { } limit
            ? $"{Format(remaining)} of {Format(limit)} {unit}"
            : $"{Format(remaining)} {unit} left";
    }

    private static string Duration(TimeSpan span) => span.TotalSeconds < 60
        ? $"{Math.Max(1, (int)Math.Ceiling(span.TotalSeconds))} s"
        : span.TotalMinutes < 60
            ? $"{(int)Math.Ceiling(span.TotalMinutes)} min"
            : $"{(int)Math.Ceiling(span.TotalHours)} h";

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
        nameof(TokenUsagePurpose.CommentSuggestion) => "Comment suggestions",
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
