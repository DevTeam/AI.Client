namespace AI.Web.Tests.Usage;

using AI.Contracts.Settings;
using AI.Contracts.Usage;
using AI.Web.Composer;
using AI.Web.Usage;
using Shouldly;
using Xunit;

public class UsagePresentationTests
{
    private readonly UsagePresentation _presentation = new(new ComposerContextPresentation(new ConnectionContextLimitsResolver()));

    [Fact]
    public void ShouldMarkAFlowThatIncludesEstimates()
    {
        _presentation.FormatFlow(Totals(42_180, 1_820)).ShouldBe("42k → 1.8k");
        _presentation.FormatFlow(Totals(42_180, 1_820) with { EstimatedRequests = 1 }).ShouldBe("≈42k → 1.8k");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("0", "$0")]
    [InlineData("0.004", "<$0.01")]
    [InlineData("0.943", "$0.94")]
    [InlineData("12.6", "$13")]
    public void ShouldFormatCost(string? cost, string? expected) =>
        _presentation.FormatCost(Totals(1, 1) with { Cost = cost is null ? null : decimal.Parse(cost, System.Globalization.CultureInfo.InvariantCulture) })
            .ShouldBe(expected);

    [Fact]
    public void ShouldGiveCacheShareAndSpeedOnlyWhenTheyMeanSomething()
    {
        _presentation.CachePercent(new TokenCounts(1_000, 10, 710)).ShouldBe(71);
        _presentation.CachePercent(new TokenCounts(0, 10)).ShouldBeNull();
        _presentation.OutputSpeed(Totals(10, 380) with { DurationMs = 10_000 }).ShouldBe(38);
        // A cached one-word answer in a blink is not a speed worth quoting.
        _presentation.OutputSpeed(Totals(10, 5) with { DurationMs = 40 }).ShouldBeNull();
    }

    [Fact]
    public void ShouldGroupTokensAsAWholeNumber() => _presentation.FormatExact(412_180).ShouldBe("412 180");

    [Fact]
    public void ShouldOrderSharesLargestFirstAndLeaveEmptyPurposesOut()
    {
        var shares = _presentation.Shares([
            new(nameof(TokenUsagePurpose.Answer), Totals(1_000, 0)),
            new(nameof(TokenUsagePurpose.Subtask), Totals(3_000, 0)),
            new(nameof(TokenUsagePurpose.Title), Totals(0, 0))
        ]);

        shares.Select(share => share.Label).ShouldBe(["Subtasks", "Answer"]);
        shares[0].Fraction.ShouldBe(0.75);
    }

    [Fact]
    public void ShouldMarkACostWorkedOutFromTheEndpointsQuotes() =>
        _presentation.FormatCost(Totals(1, 1) with { Cost = 0.36m, PricedRequests = 3, EstimatedCostRequests = 2 }).ShouldBe("≈$0.36");

    [Fact]
    public void ShouldSayWhatTheCacheCouldHaveServedAndWhatResetIt()
    {
        var totals = Totals(1_000, 10) with { ReusableInputTokens = 870, ToolChanges = 2, HistoryChanges = 1 };

        _presentation.ReusablePercent(totals).ShouldBe(87);
        _presentation.FormatPrefixChanges(totals).ShouldBe("tools 2 · history 1");
        _presentation.ReusablePercent(Totals(1_000, 10)).ShouldBeNull();
        _presentation.FormatPrefixChanges(Totals(1_000, 10)).ShouldBeNull();
    }

    [Fact]
    public void ShouldDescribeTheProvidersLimitsWhileTheyAreRecent()
    {
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var limits = new RateLimitStatus(now, new RateLimitWindow(500, 40, now.AddMinutes(6)),
            new RateLimitWindow(null, 39_000, now.AddSeconds(20)));

        _presentation.FormatRateLimits(limits, now).ShouldBe("40 of 500 requests · 39k tokens left · resets in 20 s");
        _presentation.IsRateLimitLow(limits).ShouldBeTrue();
        _presentation.FormatRateLimits(limits, now.AddHours(1)).ShouldBeNull();
        _presentation.FormatRateLimits(null, now).ShouldBeNull();
    }

    private static TokenUsageTotals Totals(long input, long output) =>
        new(new TokenCounts(input, output), 1, 0, null, 0, 1_000);
}
