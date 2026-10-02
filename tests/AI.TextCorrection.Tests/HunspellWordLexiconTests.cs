namespace AI.TextCorrection.Tests;

using Shouldly;
using Xunit;

public sealed class HunspellWordLexiconTests
{
    private readonly HunspellWordLexicon _lexicon = new(new WordLexicon(), new EmbeddedTextDictionaries());

    [Theory]
    [InlineData("en", "dictionary")]
    [InlineData("en", "dictionaries")]
    [InlineData("en", "running")]
    [InlineData("ru", "подключать")]
    [InlineData("ru", "словарями")]
    [InlineData("ru", "английском")]
    [InlineData("ru", "русском")]
    [InlineData("ru", "подсвечивает")]
    [InlineData("fr", "bonjour")]
    [InlineData("fr", "école")]
    [InlineData("fr", "familles")]
    [InlineData("es", "mañana")]
    [InlineData("es", "español")]
    [InlineData("es", "niños")]
    public void RecognizesWordsAndInflections(string language, string word) =>
        _lexicon.Contains(language, word).ShouldBeTrue();

    [Theory]
    [InlineData("en", "ghbdtn")]
    [InlineData("ru", "преветтт")]
    [InlineData("uk", "привіт")]
    public void RejectsMisspellingsAndUnsupportedLanguages(string language, string word) =>
        _lexicon.Contains(language, word).ShouldBeFalse();

    [Theory]
    [InlineData("gjlrk.xfnm", "подключать")]
    [InlineData("ckjdfhzvb", "словарями")]
    [InlineData("fyukbqcrjv", "английском")]
    public void CorrectsWordsOutsideThePreviousSmallVocabulary(string input, string expected)
    {
        var analyzer = new TextCorrectionComposition().Analyzer;
        analyzer.Analyze(input, ["en", "ru"]).Single().Text.ShouldBe(expected);
    }

    [Theory]
    [InlineData("fq;ille", "famille", "en", "fr")]
    [InlineData("2cole", "école", "en", "fr")]
    [InlineData("fq,ily", "family", "fr", "en")]
    [InlineData("ma;ana", "mañana", "en", "es")]
    [InlineData("espa;ol", "español", "en", "es")]
    public void CorrectsFrenchAndSpanishLayouts(string input, string expected, string source, string target)
    {
        new TextCorrectionComposition().Analyzer.Analyze(input, [source, target]).Single().Text.ShouldBe(expected);
    }
}
