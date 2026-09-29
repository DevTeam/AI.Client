namespace AI.Web.Tests.Resources;

using AI.Contracts.Workspace;
using AI.Web.Resources;
using Shouldly;
using Xunit;

public sealed class DiffSnapshotReaderTests
{
    private readonly DiffSnapshotReader _reader = new();

    [Fact]
    public void ShouldReadEveryFileOfTheCapturedDiff()
    {
        const string excerpt = """
            diff --git a/src/app.cs b/src/app.cs
            index 1111111..2222222 100644
            --- a/src/app.cs
            +++ b/src/app.cs
            @@ -1,3 +1,3 @@
             keep
            --- a removed line that looks like a header
            +++ an added line that looks like a header
            +added
            diff --git a/new.txt b/new.txt
            new file mode 100644
            --- /dev/null
            +++ b/new.txt
            @@ -0,0 +1 @@
            +hello
            diff --git a/old.cs b/renamed.cs
            similarity index 90%
            rename from old.cs
            rename to renamed.cs
            diff --git a/logo.png b/logo.png
            Binary files a/logo.png and b/logo.png differ

            Untracked files (not in the diff):
              notes.md
              ... and 3 more

            """;

        var snapshot = _reader.Read(excerpt, "C:\\repo\\").ShouldNotBeNull();

        snapshot.IsCut.ShouldBeTrue();
        snapshot.Changes.Files.Select(file => (file.Path, file.Kind, file.Additions, file.Deletions)).ShouldBe([
            ("C:\\repo\\src\\app.cs", FileChangeKind.Modified, 2, 1),
            ("C:\\repo\\new.txt", FileChangeKind.Added, 1, 0),
            ("C:\\repo\\renamed.cs", FileChangeKind.Renamed, 0, 0),
            ("C:\\repo\\logo.png", FileChangeKind.Modified, null, null),
            ("C:\\repo\\notes.md", FileChangeKind.Added, null, null)
        ]);
        snapshot.Changes.Files[0].Diff.ShouldStartWith("@@ -1,3 +1,3 @@\n keep\n");
        snapshot.Changes.Files[2].PreviousPath.ShouldBe("C:\\repo\\old.cs");
        snapshot.Changes.Files[3].IsBinary.ShouldBeTrue();
        (snapshot.Changes.Additions, snapshot.Changes.Deletions).ShouldBe((3, 1));
    }

    [Fact]
    public void ShouldReadNoChangesAndACutDiff()
    {
        _reader.Read("No uncommitted changes.", "/repo").ShouldNotBeNull().Changes.IsEmpty.ShouldBeTrue();
        _reader.Read(null, "/repo").ShouldBeNull();

        var cut = _reader.Read("diff --git a/a b/a\n--- a/a\n+++ b/a\n@@ -1 +1 @@\n-x\n+y\n[cut: the diff is longer; run git diff for the rest]", "/repo")
            .ShouldNotBeNull();
        cut.IsCut.ShouldBeTrue();
        cut.Changes.Files.Single().Path.ShouldBe("/repo/a");
    }
}
