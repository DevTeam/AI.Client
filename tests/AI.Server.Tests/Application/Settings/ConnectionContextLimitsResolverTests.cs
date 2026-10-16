namespace AI.Application.Tests.Settings;

using AI.Contracts.Settings;
using Shouldly;
using Xunit;

public class ConnectionContextLimitsResolverTests
{
    private static readonly ConnectionSettings Connection = new(
        Guid.CreateVersion7(), "Connection", "https://llm.example/v1", "model", true, true, false);

    [Fact]
    public void ShouldFallBackToTheSmallDefaultWhenNothingIsKnown()
    {
        var limits = new ConnectionContextLimitsResolver().Resolve(Connection);

        limits.ShouldBe(new ResolvedConnectionContextLimits(
            32_768, ContextLimitSource.Default, 4_096, ContextLimitSource.Default));
    }

    [Fact]
    public void ShouldUseTheWindowTheCatalogReportedForTheModel()
    {
        var limits = new ConnectionContextLimitsResolver().Resolve(
            Connection with { DetectedContextWindow = new DetectedContextWindow("model", 131_072) });

        limits.ContextWindowTokens.ShouldBe(131_072);
        limits.ContextWindowSource.ShouldBe(ContextLimitSource.Detected);
        limits.ReservedOutputSource.ShouldBe(ContextLimitSource.Default);
    }

    [Fact]
    public void ShouldIgnoreAWindowDetectedForAnotherModel()
    {
        var limits = new ConnectionContextLimitsResolver().Resolve(
            Connection with { DetectedContextWindow = new DetectedContextWindow("previous-model", 131_072) });

        limits.ContextWindowTokens.ShouldBe(32_768);
        limits.ContextWindowSource.ShouldBe(ContextLimitSource.Default);
    }

    [Fact]
    public void ShouldPreferAWindowSetByHandOverADetectedOne()
    {
        var limits = new ConnectionContextLimitsResolver().Resolve(Connection with
        {
            ContextWindowTokens = 8_192,
            DetectedContextWindow = new DetectedContextWindow("model", 131_072)
        });

        limits.ContextWindowTokens.ShouldBe(8_192);
        limits.ContextWindowSource.ShouldBe(ContextLimitSource.Override);
    }
}
