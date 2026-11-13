namespace AI.Web.Tests.Presentation;

using AI.Contracts.Runs;
using AI.Contracts.Schedules;
using Shouldly;
using Xunit;

public sealed class UserPromptInputsTests
{
    private readonly UserPromptInputs _inputs = new(new ScheduleCalendar());

    private static UserPromptQuestion Number(double min, double max, double? step = null, string? unit = null) =>
        new("n", "How many?", null, [], false, false, Input: new UserPromptInput(UserPromptInputKind.Number, min, max, step, unit));

    [Theory]
    [InlineData("{value * 12} files", "4", "48 files")]
    [InlineData("≈ {value / 3:2} h", "10", "≈ 3.33 h")]
    [InlineData("{value * 1000} ms", "2.5", "2,500 ms")]
    [InlineData("{(value + 1) * 2}", "4", "10")]
    public void ShouldWorkOutAPreviewFromTheNumber(string template, string value, string expected) =>
        _inputs.RenderPreview(template, new UserPromptPreviewValues([value], [], null, 1)).ShouldBe(expected);

    [Fact]
    public void ShouldReadTheEndsOfARangeAndTheChosenLabels()
    {
        _inputs.RenderPreview("{to - from} hours", new UserPromptPreviewValues(["9", "18"], [], null, 2)).ShouldBe("9 hours");
        _inputs.RenderPreview("{count} chosen: {label}", new UserPromptPreviewValues([], ["Api", "Web"], null, 2)).ShouldBe("2 chosen: Api, Web");
    }

    [Fact]
    public void ShouldShowNoPreviewUntilTheAnswerCanBeWorkedOut() =>
        _inputs.RenderPreview("{value * 2}", new UserPromptPreviewValues([], [], null, 0)).ShouldBeNull();

    [Theory]
    [InlineData("{value", "no matching")]
    [InlineData("{value * }", "operand is missing")]
    [InlineData("{price}", "unknown name 'price'")]
    [InlineData("{value:9}", "0..6 decimals")]
    public void ShouldExplainABrokenPreview(string template, string message) =>
        _inputs.ValidatePreview(template)!.ShouldContain(message);

    [Fact]
    public void ShouldSnapANumberToItsStepAndRefuseOneOffTheSlider()
    {
        var question = Number(0, 1, 0.25);

        _inputs.Normalize(question, "0.5").ShouldBe("0.5");
        _inputs.Normalize(question, "0.6").ShouldBeNull();
        _inputs.Normalize(question, "2").ShouldBeNull();
        // Whole numbers for a whole span, a round fraction for a small one.
        _inputs.StepOf(Number(0, 100).Input!).ShouldBe(1);
        _inputs.StepOf(Number(0, 1).Input!).ShouldBe(0.01, 1e-12);
    }

    [Fact]
    public void ShouldSayTheAnswerInWords()
    {
        _inputs.Describe(Number(0, 10_000, unit: "ms"), ["2500"]).ShouldBe("2,500 ms");
        _inputs.Describe(Number(0, 100, unit: "%"), ["40"]).ShouldBe("40%");
        var scale = new UserPromptQuestion("s", "How important?", null, [], false, false,
            Input: new UserPromptInput(UserPromptInputKind.Scale, Marks: [new UserPromptMark(5, "Critical")]));
        _inputs.Describe(scale, ["5"]).ShouldBe("5 — Critical");
        _inputs.Describe(scale, ["3"]).ShouldBe("3 of 5");
    }

    [Fact]
    public void ShouldSpellAColourOneWay()
    {
        var question = new UserPromptQuestion("c", "Colour?", null, [], false, false, Input: new UserPromptInput(UserPromptInputKind.Color));

        _inputs.Normalize(question, "#ABC").ShouldBe("#aabbcc");
        _inputs.Normalize(question, "3B82F6").ShouldBe("#3b82f6");
        _inputs.Normalize(question, "blue").ShouldBeNull();
    }

    [Fact]
    public void ShouldReadARangePresetAsItsTwoEnds()
    {
        var question = new UserPromptQuestion("r", "Hours?", null, [], false, false,
            Input: new UserPromptInput(UserPromptInputKind.Range, 0, 24));

        _inputs.PresetValues(question, "18..9").ShouldBe(["9", "18"]);
        _inputs.PresetValues(question, "9").ShouldBeNull();
    }
}
