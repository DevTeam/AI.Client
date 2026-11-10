namespace AI.Search.Tests;

using AI.Contracts.FileSystem;
using AI.Search.Ignoring;
using AI.Search.Matchers;
using Shouldly;
using Xunit;

/// <summary>The pattern layer on its own: globs, case rules and the two matchers.</summary>
public sealed class MatcherTests
{
    [Theory]
    [InlineData("*.cs", "a.cs", "src/a.cs", true)]
    [InlineData("*.cs", "a.txt", "src/a.txt", false)]
    [InlineData("src/**/*.cs", "a.cs", "src/a.cs", true)]
    [InlineData("src/**/*.cs", "a.cs", "src/deep/a.cs", true)]
    [InlineData("src/**/*.cs", "a.cs", "other/a.cs", false)]
    [InlineData("src/*.cs", "a.cs", "src/a.cs", true)]
    [InlineData("src/*.cs", "a.cs", "src/deep/a.cs", false)]
    [InlineData("file?.cs", "file1.cs", "file1.cs", true)]
    [InlineData("[ab].cs", "b.cs", "b.cs", true)]
    [InlineData("[!ab].cs", "b.cs", "b.cs", false)]
    public void GlobsMatchNamesOrPaths(string pattern, string name, string relative, bool expected) =>
        PathGlob.Create(pattern, ignoreCase: false).IsMatch(name, relative).ShouldBe(expected);

    [Fact]
    public void ASetWithoutGlobsMatchesNothing()
    {
        GlobSet.Empty.IsMatch("a.cs", "a.cs").ShouldBeFalse();
        GlobSet.Empty.IsEmpty.ShouldBeTrue();
    }

    [Theory]
    [InlineData("needle", 1)]
    [InlineData("NEEDLE", 0)]
    public async Task SmartCaseLooksAtThePatternItself(string pattern, int expected)
    {
        using var corpus = new SearchCorpus().File("file.txt", "Needle\n");

        var result = await SearchCorpus.RunAsync(corpus.Request(pattern));

        result.Totals.Matches.ShouldBe(expected);
    }

    [Theory]
    [InlineData("plain text", "a plain text b", 2)]
    [InlineData("missing", "a plain text b", -1)]
    public void LiteralMatcherFindsTheFirstOccurrence(string needle, string line, int expected) =>
        new LiteralLineMatcher(needle, StringComparison.Ordinal, wholeWord: false).Find(line).Index.ShouldBe(expected);

    [Fact]
    public void WordBoundaryRejectsAMatchInsideAWord()
    {
        var matcher = new LiteralLineMatcher("abc", StringComparison.Ordinal, wholeWord: true);

        matcher.Find("xabc abc").Index.ShouldBe(5);
        matcher.Find("xabc").Index.ShouldBe(-1);
        matcher.Find("(abc)").Index.ShouldBe(1);
    }

    [Fact]
    public void RegexMatcherReportsSpanIndexes()
    {
        var regex = new System.Text.RegularExpressions.Regex(@"b+");
        var matcher = new RegexLineMatcher(regex, wholeWord: false);

        var (index, length) = matcher.Find("abbbc");

        index.ShouldBe(1);
        length.ShouldBe(3);
    }

    [Theory]
    [InlineData(@"public\s+sealed\s+class\s+Generated\d+_3", "Generated")]
    [InlineData(@"needle", "needle")]
    [InlineData(@"a\.b", "a.b")]
    [InlineData(@"(x)y", null)]
    [InlineData(@"one|two", null)]
    [InlineData(@"colou?r", "colo")]
    [InlineData(@"\d+", null)]
    [InlineData(@"[abc]def", "def")]
    [InlineData(@"foo.*bar", "foo")]
    public void ARequiredLiteralIsPromisedOnlyWhenEveryMatchHoldsIt(string pattern, string? expected)
    {
        var bytes = RequiredLiteral.Find(pattern, ignoreCase: false);

        (bytes is null ? null : System.Text.Encoding.UTF8.GetString(bytes)).ShouldBe(expected);
    }

    [Fact]
    public void AnIgnoreCaseRegexPromisesNoLiteral() =>
        RequiredLiteral.Find("needle", ignoreCase: true).ShouldBeNull();

    [Fact]
    public void IgnoreRulesApplyTheLastMatchingRule()
    {
        var root = Path.Combine(Path.GetTempPath(), "AI.Search.Tests", "root");
        var paths = new SystemPath();
        var rules = IgnoreRules.Parse(["*.cs", "!keep.cs"], root, paths.Comparison);

        rules.IsIgnored(root, Path.Combine(root, "a.cs"), false, paths).ShouldBeTrue();
        rules.IsIgnored(root, Path.Combine(root, "keep.cs"), false, paths).ShouldBeFalse();
        rules.IsIgnored(root, Path.Combine(root, "a.txt"), false, paths).ShouldBeFalse();
    }

    [Fact]
    public void DirectoryOnlyRulesDoNotApplyToFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "AI.Search.Tests", "root");
        var paths = new SystemPath();
        var rules = IgnoreRules.Parse(["build/"], root, paths.Comparison);

        rules.IsIgnored(root, Path.Combine(root, "build"), true, paths).ShouldBeTrue();
        rules.IsIgnored(root, Path.Combine(root, "build"), false, paths).ShouldBeFalse();
    }

    [Fact]
    public void ACommentsAndBlankLinesAreNotRules()
    {
        IgnoreRules.Parse(["# comment", "", "   "], Path.GetTempPath(), StringComparison.Ordinal).IsEmpty.ShouldBeTrue();
    }
}