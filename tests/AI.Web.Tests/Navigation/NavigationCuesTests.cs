namespace AI.Web.Tests.Navigation;

using AI.Contracts.Navigation;
using AI.Web.Navigation;
using Shouldly;
using Xunit;

public class NavigationCuesTests
{
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly Guid Source = Guid.NewGuid();
    private static readonly Guid Target = Guid.NewGuid();
    private readonly NavigationCues _cues = new();

    [Fact]
    public void ShouldCountDownWhileTheUserWatchesTheChatThatAsked()
    {
        var cue = _cues.Decide(new AppNavigation(Project, Target, null, Source, "Hello", "Getting started"), Source, false, "Shop / Setup");

        cue.Automatic.ShouldBeTrue();
        cue.SecondsLeft.ShouldBe(_cues.CountdownSeconds);
        cue.Where.ShouldBe("chat “Getting started” in Hello");
        cue.From.ShouldBe("Shop / Setup");
    }

    [Fact]
    public void ShouldOnlyOfferTheMoveToAUserWhoIsElsewhereOrBusy()
    {
        var target = new AppNavigation(Project, Target, null, Source, "Hello", "Getting started");

        _cues.Decide(target, Guid.NewGuid(), false, "").Automatic.ShouldBeFalse();
        _cues.Decide(target, Source, true, "").Automatic.ShouldBeFalse();
        // A request that does not say where it came from is never followed on its own.
        _cues.Decide(target with { SourceChatId = null }, Source, false, "").Automatic.ShouldBeFalse();
    }

    [Fact]
    public void ShouldNameProjectsAndBranchesInWords()
    {
        _cues.Decide(new AppNavigation(Project, null, null, Source, "Hello"), Source, false, "").Where
            .ShouldBe("project Hello");
        _cues.Decide(new AppNavigation(Project, Target, Guid.NewGuid(), Source, "Hello", "Plan"), Source, false, "").Where
            .ShouldBe("a branch of “Plan” in Hello");
    }
}
