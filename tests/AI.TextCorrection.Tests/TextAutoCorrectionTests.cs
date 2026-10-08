namespace AI.TextCorrection.Tests;

using Shouldly;
using Xunit;

[Trait("Category", "Slow")]
public sealed class TextAutoCorrectionTests
{
    [Fact]
    public async Task OneLanguageCorrectsSpellingWithoutChangingLayout()
    {
        var composition = new TextCorrectionComposition();
        await composition.Preparation.PrepareAsync(["ru"]);
        Apply("првиет ghbdtn", composition.AutoCorrection.Analyze("првиет ghbdtn", ["ru"]))
            .ShouldBe("привет ghbdtn");
    }

    [Fact]
    public async Task CombinesBothKindsOfCorrectionWithOriginalOffsets()
    {
        var composition = new TextCorrectionComposition();
        await composition.Preparation.PrepareAsync(["en", "ru"]);
        var input = "helllo ghbdtn првиет!";
        Apply(input, composition.AutoCorrection.Analyze(input, ["en", "ru"]))
            .ShouldBe("hello привет привет!");
        input = "привет ghdbtn кино";
        Apply(input, composition.AutoCorrection.Analyze(input, ["en", "ru"]))
            .ShouldBe("привет привет кино");
    }

    [Fact]
    public async Task ExcludedTextCannotBeCorrectedOrSupplyLayoutContext()
    {
        var composition = new TextCorrectionComposition();
        await composition.Preparation.PrepareAsync(["en", "ru"]);
        composition.AutoCorrection.Analyze("пойдешь cj", ["en", "ru"], [new TextRange(0, 7)]).ShouldBeEmpty();
        var input = "првиет helllo";
        Apply(input, composition.AutoCorrection.Analyze(input, ["en", "ru"], [new TextRange(0, 6)]))
            .ShouldBe("првиет hello");
        composition.AutoCorrection.Analyze("пойдешь pasted cj", ["en", "ru"], [new TextRange(8, 6)]).ShouldBeEmpty();
        composition.AutoCorrection.Analyze("```\nhelllo\n```", ["en", "ru"], [new TextRange(0, 4)]).ShouldBeEmpty();
        composition.AutoCorrection.Analyze("`helllo`", ["en", "ru"], [new TextRange(0, 1)]).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("`helllo` https://example.com/helllo")]
    [InlineData("```\nhelllo\n```")]
    [InlineData("/helllo @helllo --helllo")]
    [InlineData("C:\\helllo\\file.txt /tmp/helllo helllo.cs helllo@example.com")]
    [InlineData("helllo_world helllo123 ChatCompozer HELLO")]
    [InlineData("hello привет bonjour hola")]
    public async Task PreservesProtectedAndKnownText(string input)
    {
        var composition = new TextCorrectionComposition();
        await composition.Preparation.PrepareAsync(["en"]);
        composition.AutoCorrection.Analyze(input, ["en"]).ShouldBeEmpty();
    }

    [Fact]
    public void NoLanguagesAndOversizedTextAreIgnored()
    {
        var analyzer = new TextCorrectionComposition().AutoCorrection;
        analyzer.Analyze("helllo", []).ShouldBeEmpty();
        analyzer.Analyze(new string('a', 8193), ["en"]).ShouldBeEmpty();
        analyzer.Analyze("helllo", ["uk"]).ShouldBeEmpty();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Tests use instance helpers by design.")]
    private string Apply(string text, IReadOnlyList<TextReplacement> edits)
    {
        foreach (var edit in edits.Reverse()) text = text[..edit.Start] + edit.Text + text[(edit.Start + edit.Length)..];
        return text;
    }
}
