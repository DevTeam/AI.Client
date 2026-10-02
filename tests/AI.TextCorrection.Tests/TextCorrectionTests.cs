namespace AI.TextCorrection.Tests;

using Shouldly;
using Xunit;

public sealed class TextCorrectionTests
{
    private readonly TextCorrectionComposition _composition = new();

    [Theory]
    [InlineData("ghbdtn rfr ltkf", "привет как дела")]
    [InlineData("руддщ цщкдв", "hello world")]
    [InlineData("Ghbdtn", "Привет")]
    [InlineData("GHBDTN", "ПРИВЕТ")]
    [InlineData("(ghbdtn)", "(привет)")]
    [InlineData("ghbdtn—руддщ", "привет—hello")]
    [InlineData("ghbdtn/ghbdtn", "привет/привет")]
    [InlineData("please ghbdtn", "please привет")]
    [InlineData("привет rfr ltkf", "привет как дела")]
    [InlineData("hello цщкдв!", "hello world!")]
    [InlineData("gjqltim cj vyjq d rbyj", "пойдешь со мной в кино")]
    [InlineData("пойдешь cj vyjq d кино", "пойдешь со мной в кино")]
    [InlineData("gjqltim cj vyj d rbyj", "пойдешь со мно в кино")]
    [InlineData("пойдешь cj vyj d кино", "пойдешь со мно в кино")]
    [InlineData("пойдешь cj", "пойдешь со")]
    [InlineData("пойдешь d", "пойдешь в")]
    [InlineData("рщц фку нщг?", "how are you?")]
    [InlineData("how фку нщг?", "how are you?")]
    [InlineData("рщц are нщг?", "how are you?")]
    [InlineData("how are нщг?", "how are you?")]
    [InlineData("hello рщцц world", "hello howw world")]
    [InlineData("привет что how are you?\nчто написать те,t tot&", "привет что how are you?\nчто написать тебе еще?")]
    [InlineData("что написать те,t", "что написать тебе")]
    [InlineData("что написать tot&", "что написать еще?")]
    [InlineData("helдщ world", "hello world")]
    public void CorrectsConfidentWordsAndPhrases(string input, string expected)
    {
        var text = input;
        foreach (var edit in _composition.Analyzer.Analyze(input, ["en", "ru"]).Reverse())
            text = text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text);
        text.ShouldBe(expected);
    }

    [Theory]
    [InlineData("hello привет")]
    [InlineData("db id ui di")]
    [InlineData("`ghbdtn` https://example.com/ghbdtn")]
    [InlineData("```\nghbdtn\n```")]
    [InlineData("```\nghbdtn")]
    [InlineData("~~~\nghbdtn")]
    [InlineData("`ghbdtn")]
    [InlineData("/ghbdtn @ghbdtn --ghbdtn ghbdtn.cs ghbdtn123")]
    [InlineData("ChatComposer unknownword")]
    [InlineData("rfr")]
    [InlineData("cj d vyj")]
    [InlineData("пойдешь. cj d vyj")]
    [InlineData("пойдешь\ncj d vyj")]
    [InlineData("пойдешь hello cj d vyj")]
    [InlineData("пойдешь `код` cj d vyj")]
    [InlineData("пойдешь hello world dictionary")]
    [InlineData("пойдешь vyj")]
    [InlineData("пойдешь hello vyj кино")]
    [InlineData("пойдешь qwerty кино")]
    [InlineData("пойдешь foobar кино")]
    [InlineData("пойдешь to go in кино")]
    [InlineData("рщц")]
    [InlineData("hello. фку нщг")]
    [InlineData("hello\nфку нщг")]
    [InlineData("hello привет фку нщг")]
    [InlineData("hello `code` фку нщг")]
    [InlineData("hello рщцц")]
    [InlineData("hello world! #define C# R&D var_1 123 abc123")]
    [InlineData("`те,t tot&` https://example.com/те,t")]
    public void PreservesAmbiguousAndProtectedText(string input) =>
        _composition.Analyzer.Analyze(input, ["en", "ru"]).ShouldBeEmpty();

    [Fact]
    public void OffersOnlyBundledCorrectionLanguages() =>
        _composition.Layouts.All.Select(layout => layout.Id).ShouldBe(["en", "ru", "fr", "es"]);

    [Fact]
    public void IgnoresUnavailableLayoutsInSavedPreferences() =>
        _composition.Analyzer.Analyze("ghbdtn", ["en", "ru", "uk"]).Single().Text.ShouldBe("привет");

    [Fact]
    public void ReportsOffsetsWithoutChangingPunctuation()
    {
        var edit = _composition.Analyzer.Analyze("hello ghbdtn!", ["en", "ru"]).Single();
        edit.Start.ShouldBe(6);
        edit.Length.ShouldBe(6);
        edit.Text.ShouldBe("привет");
    }

    [Fact]
    public void ConverterRoundTripsCaseAndKeyboardPunctuation()
    {
        var converter = new LayoutConverter();
        var english = _composition.Layouts.All[0];
        foreach (var target in _composition.Layouts.All.Skip(1))
        {
            var original = english.Keys + english.ShiftKeys;
            converter.Convert(converter.Convert(original, english, target), target, english).ShouldBe(original);
        }
    }

    [Fact]
    public void RequiresTwoEnabledLayouts() =>
        _composition.Analyzer.Analyze("ghbdtn", ["en"]).ShouldBeEmpty();

    [Fact]
    public void BoundsWorkForLongDocuments() =>
        _composition.Analyzer.Analyze(new string('a', 9000), ["en", "ru"]).ShouldBeEmpty();

    [Fact]
    public void LayoutMapsHaveMatchingLengths()
    {
        foreach (var layout in _composition.Layouts.All)
        {
            layout.Keys.Length.ShouldBe(_composition.Layouts.All[0].Keys.Length);
            layout.ShiftKeys.Length.ShouldBe(layout.Keys.Length);
        }
    }
}
