namespace AI.TextCorrection.Tests;

using Shouldly;
using Xunit;

public sealed class WordBoundariesTests
{
    [Theory]
    [InlineData(',')]
    [InlineData('.')]
    [InlineData(';')]
    [InlineData('\'')]
    [InlineData('[')]
    public void KeepsKeysThatCanBecomeLettersInsideWords(char character) =>
        new TextCorrectionComposition().Boundaries.AmbiguousSeparators(["en", "ru"]).ShouldContain(character);

    [Theory]
    [InlineData('!')]
    [InlineData('?')]
    [InlineData('(')]
    [InlineData(')')]
    [InlineData('-')]
    [InlineData('/')]
    public void AllowsSeparatorsThatCannotBecomeLetters(char character) =>
        new TextCorrectionComposition().Boundaries.AmbiguousSeparators(["en", "ru"]).ShouldNotContain(character);

    [Fact]
    public void UsesSelectedMapsIncludingCustomLanguages()
    {
        var boundaries = new WordBoundaries(new CustomLayouts(), new LayoutConverter());
        boundaries.AmbiguousSeparators(["first", "second"]).ShouldBe("!");
        boundaries.AmbiguousSeparators(["first"]).ShouldBeEmpty();
    }

    private sealed class CustomLayouts : IKeyboardLayouts
    {
        public IReadOnlyList<KeyboardLayout> All { get; } =
            [new("first", "First", "!a", "!A"), new("second", "Second", "αb", "ΑB")];
    }
}
