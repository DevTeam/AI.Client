namespace AI.Client.Application.Tests.Workspace;

using AI.Client.Application.Workspace;
using Shouldly;
using Xunit;

public class LineDiffTests
{
    [Fact]
    public void ShouldReportNothingForIdenticalText()
    {
        var result = LineDiff.Compare("alpha\nbeta\n", "alpha\nbeta\n");

        result.Additions.ShouldBe(0);
        result.Deletions.ShouldBe(0);
        result.Diff.ShouldBeEmpty();
        result.IsExact.ShouldBeTrue();
    }

    [Fact]
    public void ShouldCountOnlyTheLinesThatActuallyChanged()
    {
        // The point of a line diff over a byte comparison: an edit in the middle of a large file
        // costs one addition and one deletion, not the whole file.
        var before = string.Join('\n', Enumerable.Range(0, 500).Select(index => $"line {index}"));
        var after = before.Replace("line 250", "line 250 edited", StringComparison.Ordinal);

        var result = LineDiff.Compare(before, after);

        result.Additions.ShouldBe(1);
        result.Deletions.ShouldBe(1);
        result.IsExact.ShouldBeTrue();
    }

    [Fact]
    public void ShouldCountInsertionsAndDeletionsSeparately()
    {
        var result = LineDiff.Compare("a\nb\nc", "a\nx\ny\nb\nc");

        result.Additions.ShouldBe(2);
        result.Deletions.ShouldBe(0);
    }

    [Fact]
    public void ShouldTreatAnEmptySideAsAWholeFile()
    {
        LineDiff.Compare(null, "a\nb").Additions.ShouldBe(2);
        LineDiff.Compare("a\nb", null).Deletions.ShouldBe(2);
    }

    [Fact]
    public void ShouldIgnoreLineEndingStyleWhenComparing()
    {
        // Normalization is for the comparison only; neither input is written back anywhere, so a
        // file's own CRLF style is never disturbed by being measured.
        var result = LineDiff.Compare("a\r\nb\r\n", "a\nb\n");

        result.Additions.ShouldBe(0);
        result.Deletions.ShouldBe(0);
    }

    [Fact]
    public void ShouldProduceAUnifiedDiffWithContext()
    {
        var result = LineDiff.Compare("one\ntwo\nthree\nfour\nfive", "one\ntwo\nTHREE\nfour\nfive");

        result.Diff.ShouldContain("@@");
        result.Diff.ShouldContain("-three");
        result.Diff.ShouldContain("+THREE");
        result.Diff.ShouldContain(" two");
        result.Diff.ShouldContain(" four");
    }

    [Fact]
    public void ShouldFallBackToAWholesaleReplacementWhenTooDifferentToTrace()
    {
        // Past the cap the honest answer is "all of it", flagged inexact — not a wrong number.
        var before = string.Join('\n', Enumerable.Range(0, LineDiff.MaxDifference).Select(index => $"a{index}"));
        var after = string.Join('\n', Enumerable.Range(0, LineDiff.MaxDifference).Select(index => $"b{index}"));

        var result = LineDiff.Compare(before, after);

        result.IsExact.ShouldBeFalse();
        result.Additions.ShouldBe(LineDiff.MaxDifference);
        result.Deletions.ShouldBe(LineDiff.MaxDifference);
    }
}
