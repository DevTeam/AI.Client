namespace AI.Application.Tests.Workspace;

using AI.Application.Workspace;
using AI.Contracts.Workspace;
using Shouldly;
using Xunit;

public class UnifiedDiffTests
{
    [Fact]
    public void ShouldClassifyEveryLineOfAHunk()
    {
        var lines = new UnifiedDiff().Parse("@@ -1,3 +1,3 @@\n one\n-two\n+TWO\n three");

        lines.Select(line => line.Kind).ShouldBe([
            DiffLineKind.Hunk, DiffLineKind.Context, DiffLineKind.Removed,
            DiffLineKind.Added, DiffLineKind.Context,
        ]);
        // The prefix character belongs to the format, not to the file.
        lines[2].Text.ShouldBe("two");
        lines[3].Text.ShouldBe("TWO");
    }

    [Fact]
    public void ShouldNumberLinesFromTheHunkHeader()
    {
        var lines = new UnifiedDiff().Parse("@@ -12,4 +20,4 @@\n keep\n-gone\n+fresh\n keep");

        var context = lines[1];
        context.OldLine.ShouldBe(12);
        context.NewLine.ShouldBe(20);

        // A removed line exists only in the old file, an added one only in the new.
        lines[2].OldLine.ShouldBe(13);
        lines[2].NewLine.ShouldBeNull();
        lines[3].OldLine.ShouldBeNull();
        lines[3].NewLine.ShouldBe(21);

        // Each side advanced by exactly the lines that side actually has.
        lines[4].OldLine.ShouldBe(14);
        lines[4].NewLine.ShouldBe(22);
    }

    [Fact]
    public void ShouldRestartNumberingAtEachHunk()
    {
        var lines = new UnifiedDiff().Parse("@@ -1,1 +1,1 @@\n first\n@@ -80,1 +90,1 @@\n later");

        lines[1].OldLine.ShouldBe(1);
        lines[3].OldLine.ShouldBe(80);
        lines[3].NewLine.ShouldBe(90);
    }

    [Fact]
    public void ShouldKeepAnEmptyContextLineAsAnEmptyLine()
    {
        // A blank line in the source arrives as a single space: the prefix and nothing else.
        var lines = new UnifiedDiff().Parse("@@ -1,2 +1,2 @@\n \n+added");

        lines[1].Kind.ShouldBe(DiffLineKind.Context);
        lines[1].Text.ShouldBeEmpty();
    }

    [Fact]
    public void ShouldReadTheTruncationTrailerAsANote()
    {
        var lines = new UnifiedDiff().Parse("@@ -1,1 +1,1 @@\n one\n… diff truncated");

        lines[^1].Kind.ShouldBe(DiffLineKind.Note);
        lines[^1].Text.ShouldBe("… diff truncated");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ShouldReturnNothingWhenThereIsNoDiff(string? diff) =>
        new UnifiedDiff().Parse(diff).ShouldBeEmpty();

    [Fact]
    public void ShouldSurviveAHeaderItCannotRead()
    {
        // Truncation can cut a header in half, and a chat file can be hand-edited. A malformed
        // hunk must not be able to hide the lines under it.
        var lines = new UnifiedDiff().Parse("@@ nonsense @@\n keep\n+new");

        lines.Count.ShouldBe(3);
        lines[1].Kind.ShouldBe(DiffLineKind.Context);
        lines[2].Kind.ShouldBe(DiffLineKind.Added);
        lines[1].OldLine.ShouldBe(1);
    }

    [Fact]
    public void ShouldTreatUnrecognizedLinesAsNotesRatherThanDroppingThem()
    {
        var lines = new UnifiedDiff().Parse("@@ -1,1 +1,1 @@\nno prefix at all");

        lines[1].Kind.ShouldBe(DiffLineKind.Note);
        lines[1].Text.ShouldBe("no prefix at all");
    }

    [Fact]
    public void ShouldParseWhatLineDiffActuallyProduces()
    {
        // The two halves have to agree: whatever the writer emits, the reader has to classify.
        var produced = new LineDiff().Compare("one\ntwo\nthree\nfour\nfive", "one\ntwo\nTHREE\nfour\nfive").Diff;

        var lines = new UnifiedDiff().Parse(produced);

        lines.ShouldContain(line => line.Kind == DiffLineKind.Hunk);
        lines.ShouldContain(line => line.Kind == DiffLineKind.Removed && line.Text == "three");
        lines.ShouldContain(line => line.Kind == DiffLineKind.Added && line.Text == "THREE");
        lines.ShouldNotContain(line => line.Kind == DiffLineKind.Note);
    }
}
