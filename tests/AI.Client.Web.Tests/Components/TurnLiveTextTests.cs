using AI.Client.Web.Components;
using Shouldly;
using Xunit;

namespace AI.Client.Web.Tests.Components;

public sealed class TurnLiveTextTests
{
    private readonly TurnLiveText _live = new();
    private readonly Guid _turn = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GrowingTextIsShownAsItGrowsWithoutBecomingANewText()
    {
        var first = _live.Present(_turn, "Looking", false, Start);
        var grown = _live.Present(_turn, "Looking at the DI setup", false, Start.AddMilliseconds(150));

        grown.Text.ShouldBe("Looking at the DI setup");
        grown.Version.ShouldBe(first.Version);
        grown.NextCheck.ShouldBeNull();
    }

    [Fact]
    public void NextTextWaitsUntilThePreviousOneCouldBeRead()
    {
        var note = new string('a', 60); // three seconds of reading
        var shown = _live.Present(_turn, note, false, Start);

        var waiting = _live.Present(_turn, "Next step", false, Start.AddSeconds(1));
        waiting.Text.ShouldBe(note);
        waiting.Version.ShouldBe(shown.Version);
        waiting.NextCheck.ShouldBe(Start.AddSeconds(3));

        var switched = _live.Present(_turn, "Next step, grown meanwhile", false, Start.AddSeconds(3));
        switched.Text.ShouldBe("Next step, grown meanwhile");
        switched.Version.ShouldNotBe(shown.Version);
    }

    [Fact]
    public void ReadingTimeIsCountedFromWhenTheTextStoppedGrowing()
    {
        _live.Present(_turn, "Short", false, Start);
        _live.Present(_turn, "Short note", false, Start.AddSeconds(10));

        _live.Present(_turn, "Other", false, Start.AddSeconds(11)).Text.ShouldBe("Short note");
        _live.Present(_turn, "Other", false, Start.AddSeconds(11.5)).Text.ShouldBe("Other");
    }

    [Fact]
    public void TextFinishedBeforeALongToolCallIsReplacedAtOnce()
    {
        _live.Present(_turn, "Reading the project", false, Start);

        _live.Present(_turn, "Found it", false, Start.AddSeconds(30)).Text.ShouldBe("Found it");
    }

    [Fact]
    public void ReadingTimeIsCapped()
    {
        _live.Present(_turn, new string('a', 2_000), false, Start);

        _live.Present(_turn, "Next", false, Start.AddSeconds(5)).NextCheck.ShouldBe(Start.AddSeconds(6));
    }

    [Fact]
    public void HeldTextIsNotReplacedAndDoesNotScheduleAWakeUp()
    {
        _live.Present(_turn, "Keep me", false, Start);

        var held = _live.Present(_turn, "Next", true, Start.AddSeconds(30));
        held.Text.ShouldBe("Keep me");
        held.NextCheck.ShouldBeNull();

        _live.Present(_turn, "Next", false, Start.AddSeconds(31)).Text.ShouldBe("Next");
    }

    [Fact]
    public void DraftLandingAsItsNoteIsNotANewText()
    {
        var draft = _live.Present(_turn, "Checking the registrations", false, Start);

        var landed = _live.Present(_turn, "Checking the registrations\n", false, Start.AddSeconds(0.2));
        landed.Version.ShouldBe(draft.Version);
        landed.NextCheck.ShouldBeNull();
    }

    [Fact]
    public void LaggingShorterCopyDoesNotReplaceTheText()
    {
        _live.Present(_turn, "Checking the registrations", false, Start);

        _live.Present(_turn, "Checking", false, Start.AddSeconds(30)).Text.ShouldBe("Checking the registrations");
    }

    [Fact]
    public void TurnDoesNotGoBackToATextItMovedPast()
    {
        _live.Present(_turn, "First note", false, Start);
        _live.Present(_turn, "Rejected draft", false, Start.AddSeconds(30));

        // The draft was dropped by the protocol, so the first note is the newest candidate again.
        _live.Present(_turn, "First note", false, Start.AddSeconds(60)).Text.ShouldBe("Rejected draft");
    }

    [Fact]
    public void EmptyCandidateKeepsWhatIsShown()
    {
        _live.Present(_turn, "Working on it", false, Start);

        _live.Present(_turn, "  ", false, Start.AddSeconds(1)).Text.ShouldBe("Working on it");
    }

    [Fact]
    public void NoRunningTurnShowsNothingAndForgetsTheTurn()
    {
        _live.Present(_turn, "Working on it", false, Start);

        _live.Present(null, "Working on it", false, Start.AddSeconds(1)).Text.ShouldBeNull();
        _live.Present(_turn, "Fresh", false, Start.AddSeconds(1.1)).Text.ShouldBe("Fresh");
    }

    [Fact]
    public void NewTurnStartsWithoutWaitingForThePreviousOne()
    {
        _live.Present(_turn, new string('a', 100), false, Start);

        _live.Present(Guid.NewGuid(), "Next turn", false, Start.AddSeconds(0.1)).Text.ShouldBe("Next turn");
    }
}
