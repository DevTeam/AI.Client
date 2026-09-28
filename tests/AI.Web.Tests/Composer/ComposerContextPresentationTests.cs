namespace AI.Web.Tests.Composer;

using AI.Contracts.Runs;
using AI.Contracts.Settings;
using AI.Web.Composer;
using Shouldly;
using Xunit;

public class ComposerContextPresentationTests
{
    private readonly ComposerContextPresentation _presentation = new(new ConnectionContextLimitsResolver());

    [Fact]
    public void ShouldLayMeasuredLayersAndDraftInDrawingOrder()
    {
        var usage = new ContextUsage(128_000, ContextLimitSource.Override, 3_000, 6_000, 40_000, 8_000, 1_280, false, 0);
        var connection = Connection(128_000, 8_000);

        var context = _presentation.Build(usage, connection, "Hello");

        context.Layers.Select(layer => layer.Kind).ShouldBe([
            ContextLayerKind.Instructions, ContextLayerKind.Tools, ContextLayerKind.History,
            ContextLayerKind.Draft, ContextLayerKind.ReservedOutput, ContextLayerKind.Overhead]);
        context.IsMeasured.ShouldBeTrue();
        context.IsDefaultCapacity.ShouldBeFalse();
        context.Layers.Single(layer => layer.Kind == ContextLayerKind.Draft).Tokens.ShouldBe(12 + 2 + 3);
        context.UsedTokens.ShouldBe(3_000 + 6_000 + 40_000 + 17 + 8_000 + 1_280);
    }

    [Fact]
    public void ShouldFollowTheSelectedConnectionForWindowAndReserve()
    {
        var usage = new ContextUsage(32_768, ContextLimitSource.Default, 1_000, 1_000, 1_000, 4_096, 1_280, false, 0);

        var context = _presentation.Build(usage, Connection(200_000, 16_000), string.Empty);

        context.CapacityTokens.ShouldBe(200_000);
        context.Layers.Single(layer => layer.Kind == ContextLayerKind.ReservedOutput).Tokens.ShouldBe(16_000);
    }

    [Fact]
    public void ShouldShowOnlyKnownLayersBeforeTheFirstRequest()
    {
        var context = _presentation.Build(null, null, string.Empty);

        context.IsMeasured.ShouldBeFalse();
        context.IsDefaultCapacity.ShouldBeTrue();
        context.Layers.Where(layer => !layer.Reserved).ShouldAllBe(layer => layer.Tokens == 0);
        context.Layers.Single(layer => layer.Kind == ContextLayerKind.Overhead).Tokens.ShouldBe(1_280);
    }

    [Theory]
    [InlineData(70_000, ContextFillLevel.Normal)]
    [InlineData(80_000, ContextFillLevel.Warning)]
    [InlineData(95_000, ContextFillLevel.Critical)]
    [InlineData(150_000, ContextFillLevel.Critical)]
    public void ShouldRaiseTheLevelAsTheWindowFills(long history, ContextFillLevel expected)
    {
        var usage = new ContextUsage(100_000, ContextLimitSource.Override, 0, 0, history, 0, 0, false, 0);

        var context = _presentation.Build(usage, Connection(100_000, 256), string.Empty);

        context.Level.ShouldBe(expected);
        context.Percent.ShouldBeLessThanOrEqualTo(100);
    }

    [Theory]
    [InlineData(950, "950")]
    [InlineData(1_250, "1.3k")]
    [InlineData(53_400, "53k")]
    [InlineData(1_100_000, "1.1M")]
    public void ShouldFormatTokensCompactly(long tokens, string expected) =>
        _presentation.FormatTokens(tokens).ShouldBe(expected);

    private static ConnectionSettings Connection(long window, long reserve) =>
        new(Guid.NewGuid(), "Test", "https://example.test/v1", "model", true, true, false, ContextWindowTokens: window, ReservedOutputTokens: reserve);
}
