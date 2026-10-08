namespace AI.Web.Tests.Composer;

using AI.Web.Composer;
using Shouldly;
using Xunit;

public class ComposerHistoryNavigatorTests
{
    private static readonly string[] History = ["newest", "middle", "oldest"];

    private readonly ComposerHistoryNavigator _navigator = CreateInstance();

    private static ComposerHistoryNavigator CreateInstance() => new();

    [Fact]
    public void ShouldStartAtTheNewestEntry()
    {
        _navigator.MoveOlder(History, string.Empty).ShouldBe("newest");

        _navigator.Index.ShouldBe(0);
        _navigator.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void ShouldNotEnterHistoryWhileTheFieldHoldsTheUsersText()
    {
        // Up is a history key only in an empty field: with text in it the key belongs to the caret,
        // and swapping the message the user is writing for an older one would lose that text.
        _navigator.MoveOlder(History, "half-typed").ShouldBeNull();

        _navigator.Index.ShouldBeNull();
        _navigator.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void ShouldKeepWalkingBackWhileTheFieldShowsAnEntry()
    {
        _navigator.MoveOlder(History, string.Empty);

        // The field is not empty any more — it holds the entry just substituted — so the text check
        // must guard entering history, not the walk the user is already in the middle of.
        _navigator.MoveOlder(History, "newest").ShouldBe("middle");
        _navigator.MoveOlder(History, "middle").ShouldBe("oldest");
    }

    [Fact]
    public void ShouldWalkBackwardsOneEntryAtATime()
    {
        _navigator.MoveOlder(History, string.Empty);

        _navigator.MoveOlder(History, string.Empty).ShouldBe("middle");
        _navigator.MoveOlder(History, string.Empty).ShouldBe("oldest");
    }

    [Fact]
    public void ShouldStayOnTheOldestEntryInsteadOfWrapping()
    {
        for (var press = 0; press < History.Length; press++) _navigator.MoveOlder(History, string.Empty);

        // Null means "the key changed nothing" — wrapping round to an empty field would read as the
        // text vanishing on a keypress that was meant to go further back.
        _navigator.MoveOlder(History, string.Empty).ShouldBeNull();
        _navigator.Index.ShouldBe(History.Length - 1);
    }

    [Fact]
    public void ShouldEmptyTheFieldWhenSteppingPastTheNewestEntry()
    {
        _navigator.MoveOlder(History, string.Empty);

        // Browsing starts in an empty field, so leaving it returns the field to empty — there is no
        // typed text to put back, which is exactly what the empty-field rule buys.
        _navigator.MoveNewer(History).ShouldBe(string.Empty);
        _navigator.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void ShouldKeepBrowsingWhileWalkingSeveralEntriesBack()
    {
        _navigator.MoveOlder(History, string.Empty);
        _navigator.MoveOlder(History, string.Empty);
        _navigator.MoveOlder(History, string.Empty);

        _navigator.MoveNewer(History).ShouldBe("middle");
        _navigator.MoveNewer(History).ShouldBe("newest");
        _navigator.MoveNewer(History).ShouldBe(string.Empty);
    }

    [Fact]
    public void ShouldDoNothingOnDownWhenNotBrowsing()
    {
        _navigator.MoveNewer(History).ShouldBeNull();

        _navigator.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void ShouldDoNothingOnUpWhenHistoryIsEmpty()
    {
        _navigator.MoveOlder([], string.Empty).ShouldBeNull();

        _navigator.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void ShouldEmptyTheFieldOnEscape()
    {
        _navigator.MoveOlder(History, string.Empty);
        _navigator.MoveOlder(History, string.Empty);

        _navigator.Exit().ShouldBe(string.Empty);
        _navigator.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void ShouldIgnoreEscapeWhenNotBrowsing()
    {
        // The composer's Escape has other owners; history must only claim it while it is actually
        // showing an entry.
        _navigator.Exit().ShouldBeNull();
    }

    [Fact]
    public void ShouldForgetTheBrowsingPositionAfterReset()
    {
        _navigator.MoveOlder(History, string.Empty);

        // Reset stands for "the composer moved on" — edited, sent, or a different chat.
        _navigator.Reset();

        _navigator.IsActive.ShouldBeFalse();
        _navigator.Exit().ShouldBeNull();
        _navigator.MoveOlder(History, string.Empty).ShouldBe("newest");
        _navigator.MoveNewer(History).ShouldBe(string.Empty);
    }

    [Fact]
    public void ShouldClampWhenTheHistoryShrankWhileBrowsing()
    {
        // A send reorders the list under the browsing position (move-to-front), and a project switch
        // can replace it outright. Neither may throw on the next arrow press.
        _navigator.MoveOlder(History, string.Empty);
        _navigator.MoveOlder(History, string.Empty);

        _navigator.MoveNewer(["only one"]).ShouldBe("only one");
        _navigator.Index.ShouldBe(0);
    }
}
