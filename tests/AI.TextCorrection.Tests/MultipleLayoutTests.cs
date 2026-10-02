namespace AI.TextCorrection.Tests;

using System.Text;
using Shouldly;
using Xunit;

public sealed class MultipleLayoutTests
{
    [Fact]
    public void ChoosesUniqueDictionaryMatchAmongThreeLayouts()
    {
        var analyzer = new TextCorrectionAnalyzer(new TestLayouts(), new LayoutConverter(), new TestLexicon(false), new TestPlausibility(), new WordBoundaries(new TestLayouts(), new LayoutConverter()));
        var edit = analyzer.Analyze("abcde", ["latin-test", "greek-test", "cyrillic-test"]).Single();
        edit.Text.ShouldBe("αβγδε");
        edit.LayoutId.ShouldBe("greek-test");
    }

    [Fact]
    public void LeavesDifferentDictionaryMatchesAmbiguous()
    {
        var analyzer = new TextCorrectionAnalyzer(new TestLayouts(), new LayoutConverter(), new TestLexicon(true), new TestPlausibility(), new WordBoundaries(new TestLayouts(), new LayoutConverter()));
        analyzer.Analyze("abcde", ["latin-test", "greek-test", "cyrillic-test"]).ShouldBeEmpty();
    }

    [Fact]
    public void KeyboardVariantUsesItsConfiguredLanguageDictionary()
    {
        var maps = new TestLayouts().All.Select(layout => layout with { LanguageId = layout.Id + "-vocabulary" }).ToArray();
        var analyzer = new TextCorrectionAnalyzer(new VariantLayouts(maps), new LayoutConverter(), new TestLexicon(false, "-vocabulary"), new TestPlausibility(), new WordBoundaries(new VariantLayouts(maps), new LayoutConverter()));
        analyzer.Analyze("abcde", ["latin-test", "greek-test", "cyrillic-test"]).Single().Text.ShouldBe("αβγδε");
    }

    [Fact]
    public void LoadsDictionaryAndLetterModelWithoutLanguageSpecificConfiguration()
    {
        var sources = new TestDictionaries();
        var lexicon = new HunspellWordLexicon(new WordLexicon(), sources);
        lexicon.Contains("custom-language", "hello").ShouldBeTrue();
        lexicon.Contains("custom-language", "ghbdtn").ShouldBeFalse();
        lexicon.Contains("ru", "привет").ShouldBeFalse();
        var model = new DictionaryWordPlausibility(sources);
        model.Score("custom-language", "hello").ShouldBe(1);
        model.Score("custom-language", "zzzzz").ShouldBe(0);
    }

    private sealed class TestLayouts : IKeyboardLayouts
    {
        public IReadOnlyList<KeyboardLayout> All { get; } =
        [
            new("latin-test", "Latin", "abcdefghijklmnopqrstuvwxyz", "ABCDEFGHIJKLMNOPQRSTUVWXYZ"),
            new("greek-test", "Greek", "αβγδεζηθικλμνξοπρστυφχψωаб", "ΑΒΓΔΕΖΗΘΙΚΛΜΝΞΟΠΡΣΤΥΦΧΨΩАБ"),
            new("cyrillic-test", "Cyrillic", "абвгдежзийклмнопрстуфхцчшщ", "АБВГДЕЖЗИЙКЛМНОПРСТУФХЦЧШЩ")
        ];
    }

    private sealed class VariantLayouts(IReadOnlyList<KeyboardLayout> layouts) : IKeyboardLayouts
    {
        public IReadOnlyList<KeyboardLayout> All { get; } = layouts;
    }

    private sealed class TestLexicon(bool ambiguous, string suffix = "") : IWordLexicon
    {
        public bool Contains(string layoutId, string word) => layoutId == "greek-test" + suffix && word == "αβγδε"
            || ambiguous && layoutId == "cyrillic-test" + suffix && word == "абвгд";
    }

    private sealed class TestPlausibility : IWordPlausibility
    {
        public double Score(string layoutId, string word) => 0;
    }

    private sealed class TestDictionaries : ITextDictionaries
    {
        public IReadOnlyList<ITextDictionaryResource> All { get; } = [new TestDictionaryResource()];
    }

    private sealed class TestDictionaryResource : ITextDictionaryResource
    {
        public string LanguageId => "custom-language";
        public Stream OpenWords() => new MemoryStream(Encoding.UTF8.GetBytes("3\nhello\nworld\ndictionary\n"));
        public Stream OpenAffixes() => new MemoryStream(Encoding.UTF8.GetBytes("SET UTF-8\n"));
    }
}
