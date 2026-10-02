namespace AI.TextCorrection.Tests;

using Shouldly;
using Xunit;

public sealed class SupportedCorrectionLayoutsTests
{
    [Fact]
    public void DiscoversSupportedLanguagesAndKeyboardVariantsWithoutConfiguredIds()
    {
        var layouts = new TestLayouts([
            new("first-layout", "First", "abc", "ABC") { LanguageId = "first-language" },
            new("variant-layout", "Variant", "bac", "BAC") { LanguageId = "first-language" },
            new("second-layout", "Second", "αβγ", "ΑΒΓ") { LanguageId = "second-language" },
            new("missing-dictionary", "Missing", "абв", "АБВ") { LanguageId = "missing-language" }
        ]);
        var dictionaries = new TestDictionaries([new Resource("first-language"), new Resource("second-language")]);
        new SupportedCorrectionLayouts(layouts, dictionaries).Ids
            .ShouldBe(["first-layout", "variant-layout", "second-layout"]);
    }

    [Fact]
    public void DoesNotSelectLayoutsWhenNoDictionariesAreAvailable()
    {
        new SupportedCorrectionLayouts(new KeyboardLayouts(), new TestDictionaries([])).Ids.ShouldBeEmpty();
    }

    private sealed record TestLayouts(IReadOnlyList<KeyboardLayout> All) : IKeyboardLayouts;
    private sealed record TestDictionaries(IReadOnlyList<ITextDictionaryResource> All) : ITextDictionaries;
    private sealed record Resource(string LanguageId) : ITextDictionaryResource
    {
        public Stream OpenWords() => throw new InvalidOperationException("Discovery must not read dictionary contents.");
        public Stream OpenAffixes() => throw new InvalidOperationException("Discovery must not read dictionary contents.");
    }
}
