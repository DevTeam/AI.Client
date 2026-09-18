namespace AI.Client.Web.Tests.Components;

using AI.Client.Web.Components;
using Shouldly;
using Xunit;

public sealed class StreamingPlacementTests
{
    [Fact]
    public void ShouldKeepShortOpeningTextProvisionalWithoutATimeLimit()
    {
        StreamingPlacement.ShouldPromote(TimeSpan.FromMinutes(1), "Now updating the repository")
            .ShouldBeFalse();
    }

    [Fact]
    public void ShouldReleaseLongAnswerAfterMinimumDelay()
    {
        var answer = new string('a', StreamingPlacement.EarlyReleaseCharacterCount);

        StreamingPlacement.ShouldPromote(StreamingPlacement.MinimumDelay, answer).ShouldBeTrue();
    }

    [Fact]
    public void ShouldNotReleaseLongAnswerBeforeMinimumDelay()
    {
        var answer = new string('a', StreamingPlacement.EarlyReleaseCharacterCount + 20);

        StreamingPlacement.ShouldPromote(StreamingPlacement.MinimumDelay - TimeSpan.FromMilliseconds(1), answer)
            .ShouldBeFalse();
    }

    [Fact]
    public void ShouldCountUnicodeCharactersAndIgnoreWhitespace()
    {
        StreamingPlacement.VisibleCharacterCount("  Привет 👋\nмир  ").ShouldBe(10);
    }

    [Fact]
    public void ShouldFlattenProvisionalTextIntoTheFixedHeightSummary()
    {
        StreamingPlacement.RunningTitle("  Now\nupdating   files  ", "2s")
            .ShouldBe("Now updating files · 2s");
    }
}
