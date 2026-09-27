namespace AI.Web.Tests.Composer;

using AI.Web.Composer;
using Shouldly;
using Xunit;

public class ComposerHistoryNavigatorTests
{
    private static readonly string[] History = ["newest", "middle", "oldest"];

    [Fact]
    public void ShouldStartAtTheNewestEntry()
    {
        var navigator = new ComposerHistoryNavigator();

        navigator.MoveOlder(History, "half-typed").ShouldBe("newest");
        navigator.Index.ShouldBe(0);
        navigator.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void ShouldWalkBackwardsOneEntryAtATime()
    {
        var navigator = new ComposerHistoryNavigator();

        navigator.MoveOlder(History, string.Empty);
        navigator.MoveOlder(History, string.Empty).ShouldBe("middle");
        navigator.MoveOlder(History, string.Empty).ShouldBe("oldest");
    }

    [Fact]
    public void ShouldStayOnTheOldestEntryInsteadOfWrapping()
    {
        var navigator = new ComposerHistoryNavigator();
        for (var press = 0; press < History.Length; press++) navigator.MoveOlder(History, string.Empty);

        // Null means "the key changed nothing" — wrapping round to the draft would read as the
        // text vanishing on a keypress that was meant to go further back.
        navigator.MoveOlder(History, string.Empty).ShouldBeNull();
        navigator.Index.ShouldBe(History.Length - 1);
    }

    [Fact]
    public void ShouldReturnTheDraftWhenSteppingPastTheNewestEntry()
    {
        var navigator = new ComposerHistoryNavigator();
        navigator.MoveOlder(History, "half-typed");

        navigator.MoveNewer(History).ShouldBe("half-typed");
        navigator.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void ShouldKeepTheDraftCapturedOnEntryWhileWalkingSeveralEntriesBack()
    {
        var navigator = new ComposerHistoryNavigator();
        navigator.MoveOlder(History, "half-typed");
        navigator.MoveOlder(History, string.Empty);
        navigator.MoveOlder(History, string.Empty);

        navigator.MoveNewer(History).ShouldBe("middle");
        navigator.MoveNewer(History).ShouldBe("newest");
        navigator.MoveNewer(History).ShouldBe("half-typed");
    }

    [Fact]
    public void ShouldDoNothingOnDownWhenNotBrowsing()
    {
        var navigator = new ComposerHistoryNavigator();

        navigator.MoveNewer(History).ShouldBeNull();
        navigator.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void ShouldDoNothingOnUpWhenHistoryIsEmpty()
    {
        var navigator = new ComposerHistoryNavigator();

        navigator.MoveOlder([], "half-typed").ShouldBeNull();
        navigator.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void ShouldRestoreTheDraftOnEscape()
    {
        var navigator = new ComposerHistoryNavigator();
        navigator.MoveOlder(History, "half-typed");
        navigator.MoveOlder(History, string.Empty);

        navigator.Exit().ShouldBe("half-typed");
        navigator.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void ShouldIgnoreEscapeWhenNotBrowsing()
    {
        // The composer's Escape has other owners; history must only claim it while it is
        // actually showing an entry.
        new ComposerHistoryNavigator().Exit().ShouldBeNull();
    }

    [Fact]
    public void ShouldForgetTheDraftAfterReset()
    {
        var navigator = new ComposerHistoryNavigator();
        navigator.MoveOlder(History, "half-typed");

        // Reset stands for "the composer moved on" — edited, sent, or a different chat.
        navigator.Reset();

        navigator.IsActive.ShouldBeFalse();
        navigator.Exit().ShouldBeNull();
        navigator.MoveOlder(History, "something else").ShouldBe("newest");
        navigator.MoveNewer(History).ShouldBe("something else");
    }

    [Fact]
    public void ShouldClampWhenTheHistoryShrankWhileBrowsing()
    {
        // A send reorders the list under the browsing position (move-to-front), and a project
        // switch can replace it outright. Neither may throw on the next arrow press.
        var navigator = new ComposerHistoryNavigator();
        navigator.MoveOlder(History, "half-typed");
        navigator.MoveOlder(History, string.Empty);

        navigator.MoveNewer(["only one"]).ShouldBe("only one");
        navigator.Index.ShouldBe(0);
    }
}
