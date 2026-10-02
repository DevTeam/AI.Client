namespace AI.TextCorrection.Tests;

using Shouldly;
using Xunit;

public sealed class TextTokenizerTests
{
    private readonly TextTokenizer _tokenizer = new();

    [Theory]
    [InlineData("ghbdtn `code` tail", 7, 6)]
    [InlineData("x ```one\n~~~two``` y", 2, 16)]
    [InlineData("x ~~~one\n```two~~~ y", 2, 16)]
    [InlineData("x `unfinished", 2, 11)]
    [InlineData("x ```unfinished", 2, 13)]
    [InlineData("x https://example.com/path y", 2, 24)]
    [InlineData("x /command! y", 2, 9)]
    [InlineData("x @name y", 2, 5)]
    [InlineData("x --argument y", 2, 10)]
    public void PreservesProtectedOffsets(string text, int start, int length) =>
        _tokenizer.ProtectedRanges(text).ShouldBe([new TextRange(start, length)]);

    [Fact]
    public void FindsMultipleProtectedRegionsInOrder() =>
        _tokenizer.ProtectedRanges("`a` @b /c --d").ShouldBe([
            new TextRange(0, 3), new TextRange(4, 2), new TextRange(7, 2), new TextRange(10, 3)]);

    [Theory]
    [InlineData("/ @ -- http:// https://")]
    [InlineData("word/command word@name word--argument")]
    public void PreservesUnmatchedPrefixes(string text) => _tokenizer.ProtectedRanges(text).ShouldBeEmpty();

    [Fact]
    public void DistinguishesInteriorSeparatorsFromChangedTerminalKeys()
    {
        var text = "(ghbdtn)—tot& / foo&bar; end&\n";
        _tokenizer.Tokenize(text, "", "&", []).ShouldBe([
            new TextToken(1, 6, "ghbdtn"), new TextToken(9, 4, "tot&"),
            new TextToken(16, 3, "foo"), new TextToken(20, 3, "bar"), new TextToken(25, 4, "end&")]);
    }

    [Fact]
    public void PreservesAmbiguousKeyboardPunctuationAndUnicodeWhitespace()
    {
        var text = "те,t\u00a0tot&\u2003abc.def";
        _tokenizer.Tokenize(text, ",.", "&", []).Select(token => token.Value).ShouldBe(["те,t", "tot&", "abc.def"]);
    }

    [Fact]
    public void DropsWholeTokensOverlappingProtectedRanges()
    {
        var text = "abc`hidden`def normal";
        _tokenizer.Tokenize(text, "`", "", _tokenizer.ProtectedRanges(text)).ShouldBe([new TextToken(15, 6, "normal")]);
    }
}
