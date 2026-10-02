namespace AI.TextCorrection.Tests;

using Shouldly;
using Xunit;

public sealed class SpellingCorrectionTests
{
    [Theory]
    [InlineData("helllo", "hello")]
    [InlineData("heloo", "hello")]
    [InlineData("hell", "hello")]
    [InlineData("helloo", "hello")]
    [InlineData("hlelo", "hello")]
    [InlineData("Hlelo", "Hello")]
    public void AcceptsUniqueSingleEditsAndPreservesSentenceCase(string word, string expected)
    {
        var dictionary = new FakeDictionary(new Dictionary<string, string[]> { ["en"] = ["hello"] });
        new SpellingCorrection(dictionary, dictionary).Correct(word, ["en"])!.Text.ShouldBe(expected);
    }

    [Theory]
    [InlineData("helo")]
    [InlineData("speling")]
    public void PreservesAmbiguousSuggestions(string word)
    {
        var dictionary = new FakeDictionary(new Dictionary<string, string[]> { ["en"] = ["hello", "help", "spelling", "spieling"] });
        new SpellingCorrection(dictionary, dictionary).Correct(word, ["en"]).ShouldBeNull();
    }

    [Fact]
    public void PreservesWordsRecognizedInAnySelectedLanguage()
    {
        var dictionary = new FakeDictionary(new Dictionary<string, string[]> { ["en"] = ["hello"], ["es"] = ["helllo"] });
        new SpellingCorrection(dictionary, dictionary).Correct("helllo", ["en", "es"]).ShouldBeNull();
        dictionary.SuggestionCalls.ShouldBe(0);
    }

    [Fact]
    public void RejectsAmbiguityAcrossLanguages()
    {
        var dictionary = new FakeDictionary(new Dictionary<string, string[]> { ["en"] = ["hello"], ["fr"] = ["hella"] });
        new SpellingCorrection(dictionary, dictionary).Correct("hellx", ["en", "fr"]).ShouldBeNull();
    }

    [Theory]
    [InlineData("HELO")]
    [InlineData("ChatCompozer")]
    [InlineData("helo123")]
    [InlineData("teh")]
    [InlineData("qxzv")]
    [InlineData("he-llo")]
    public void LeavesAcronymsIdentifiersShortWordsAndDistantSuggestionsAlone(string word)
    {
        var dictionary = new FakeDictionary(new Dictionary<string, string[]> { ["en"] = ["hello"] });
        new SpellingCorrection(dictionary, dictionary).Correct(word, ["en"]).ShouldBeNull();
    }

    [Fact]
    public void CachesResultsByWordAndLanguageSetIncludingNegativeResults()
    {
        var dictionary = new FakeDictionary(new Dictionary<string, string[]> { ["en"] = ["hello"] });
        var correction = new SpellingCorrection(dictionary, dictionary);
        correction.Correct("helllo", ["en"])!.Text.ShouldBe("hello");
        correction.Correct("helllo", ["en"])!.Text.ShouldBe("hello");
        dictionary.SuggestionCalls.ShouldBe(1);
        correction.Correct("qxzv", ["en"]).ShouldBeNull();
        correction.Correct("qxzv", ["en"]).ShouldBeNull();
        dictionary.SuggestionCalls.ShouldBe(2);
        correction.Correct("helllo", ["en", "fr"])!.Text.ShouldBe("hello");
        dictionary.SuggestionCalls.ShouldBe(4);
    }

    [Theory]
    [InlineData("en", "helllo", "hello")]
    [InlineData("en", "dictioanry", "dictionary")]
    [InlineData("ru", "првиет", "привет")]
    [InlineData("ru", "правопсиание", "правописание")]
    [InlineData("fr", "bonojur", "bonjour")]
    [InlineData("es", "espñaol", "español")]
    public async Task UsesTheRealEmbeddedDictionary(string language, string word, string expected)
    {
        var composition = new TextCorrectionComposition();
        await composition.Preparation.PrepareAsync([language]);
        composition.AutoCorrection.Analyze(word, [language]).Single().Text.ShouldBe(expected);
    }

    private sealed class FakeDictionary(Dictionary<string, string[]> words) : IWordLexicon, IWordSuggestions
    {
        public int SuggestionCalls { get; private set; }
        public bool Contains(string languageId, string word) => words.TryGetValue(languageId, out var known)
            && known.Contains(word, StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<string> Suggest(string languageId, string word)
        {
            SuggestionCalls++;
            return words.GetValueOrDefault(languageId) ?? [];
        }
    }
}
