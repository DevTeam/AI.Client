namespace AI.Infrastructure.Tests.Storage;

using AI.Infrastructure.Storage;
using Shouldly;
using Xunit;

public sealed class PhysicalDirectoryBrowserTests : IDisposable
{
    private readonly PhysicalDirectoryBrowser _browser = new();
    private readonly string _root = Directory.CreateTempSubdirectory("dir-browser-tests").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task ListsSubdirectoriesInOrderAndLeavesFilesOut()
    {
        Directory.CreateDirectory(Path.Combine(_root, "beta"));
        Directory.CreateDirectory(Path.Combine(_root, "Alpha"));
        await File.WriteAllTextAsync(Path.Combine(_root, "notes.txt"), "x", TestContext.Current.CancellationToken);

        var listing = await _browser.ListAsync(_root, false, TestContext.Current.CancellationToken);

        listing.ShouldNotBeNull();
        listing.IsAccessible.ShouldBeTrue();
        listing.Directories.Select(item => item.Name).ShouldBe(["Alpha", "beta"]);
    }

    // Files cost an extra enumeration and are noise between the caller and the folder they are
    // heading for, so they arrive only when asked for.
    [Fact]
    public async Task ListsFilesOnlyWhenTheyAreAskedFor()
    {
        Directory.CreateDirectory(Path.Combine(_root, "child"));
        await File.WriteAllTextAsync(Path.Combine(_root, "notes.txt"), "x", TestContext.Current.CancellationToken);

        var without = await _browser.ListAsync(_root, false, TestContext.Current.CancellationToken);
        var with = await _browser.ListAsync(_root, true, TestContext.Current.CancellationToken);

        without!.Files.ShouldBeEmpty();
        with!.Files.ShouldHaveSingleItem().Name.ShouldBe("notes.txt");
        with.Directories.ShouldHaveSingleItem().Name.ShouldBe("child");
    }

    [Fact]
    public async Task TellsAFileApartFromADirectory()
    {
        var file = Path.Combine(_root, "notes.txt");
        await File.WriteAllTextAsync(file, "x", TestContext.Current.CancellationToken);

        var probe = await _browser.ResolveAsync(file, TestContext.Current.CancellationToken);

        probe.FileExists.ShouldBeTrue();
        probe.DirectoryExists.ShouldBeFalse();
    }

    [Fact]
    public async Task ReportsNothingForAPathThatIsNotADirectory()
    {
        var file = Path.Combine(_root, "notes.txt");
        await File.WriteAllTextAsync(file, "x", TestContext.Current.CancellationToken);

        (await _browser.ListAsync(file, false, TestContext.Current.CancellationToken)).ShouldBeNull();
        (await _browser.ListAsync(Path.Combine(_root, "absent"), false, TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task PointsUpOneLevel()
    {
        var child = Directory.CreateDirectory(Path.Combine(_root, "child")).FullName;

        var listing = await _browser.ListAsync(child, false, TestContext.Current.CancellationToken);

        listing!.ParentPath.ShouldBe(_root);
    }

    [Fact]
    public async Task ListsTheRootsAsTheLevelAboveEverything()
    {
        var listing = await _browser.ListRootsAsync(TestContext.Current.CancellationToken);

        listing.CurrentPath.ShouldBeEmpty();
        listing.ParentPath.ShouldBeNull();
        listing.Directories.ShouldNotBeEmpty();
    }

    // The empty path is the roots level, which is where "up" from a drive leads.
    [Fact]
    public async Task TreatsTheEmptyPathAsTheRootsLevel()
    {
        var listing = await _browser.ListAsync(string.Empty, false, TestContext.Current.CancellationToken);

        listing!.CurrentPath.ShouldBeEmpty();
        listing.ParentPath.ShouldBeNull();
    }

    // One directory must have one spelling, or revoking the grant you can see leaves its twin
    // in force.
    [Fact]
    public void CanonicalisesTheSpellingsOfOneDirectoryToOnePath()
    {
        var child = Directory.CreateDirectory(Path.Combine(_root, "child")).FullName;
        var roundabout = Path.Combine(_root, "child", "..", "child");

        _browser.Canonicalize(roundabout).ShouldBe(child);
        _browser.Canonicalize($" {child}{Path.DirectorySeparatorChar} ").ShouldBe(child);
    }

    [Fact]
    public void KeepsARootsTrailingSeparatorBecauseItIsPartOfTheRoot()
    {
        var root = Path.GetPathRoot(_root)!;

        _browser.Canonicalize(root).ShouldBe(root);
    }

    // Resolving a relative path would anchor it to the host process's working directory — a place
    // the user never named and cannot see — so it is left exactly as typed.
    [Fact]
    public void LeavesAPathThatIsNotFullyQualifiedAlone()
    {
        _browser.Canonicalize("some/relative/path").ShouldBe("some/relative/path");
        _browser.Canonicalize(string.Empty).ShouldBeEmpty();
    }

    [Fact]
    public async Task ResolvesAPathThatIsNotThereWithoutRefusingIt()
    {
        var absent = Path.Combine(_root, "later");

        var probe = await _browser.ResolveAsync(absent, TestContext.Current.CancellationToken);

        probe.CanonicalPath.ShouldBe(absent);
        probe.DirectoryExists.ShouldBeFalse();
        probe.IsFullyQualified.ShouldBeTrue();
    }

    [Fact]
    public async Task ReportsAPathThatNamesNoRoot()
    {
        var probe = await _browser.ResolveAsync("some/relative/path", TestContext.Current.CancellationToken);

        probe.IsFullyQualified.ShouldBeFalse();
    }

    [Fact]
    public async Task ResolvesAPathThatIsThere()
    {
        var probe = await _browser.ResolveAsync(_root, TestContext.Current.CancellationToken);

        probe.CanonicalPath.ShouldBe(_root);
        probe.DirectoryExists.ShouldBeTrue();
        probe.FileExists.ShouldBeFalse();
    }

    [Fact]
    public async Task ExpandsTheHomeShorthandPeoplePaste()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.SkipWhen(home.Length == 0, "This account has no user profile directory.");

        var probe = await _browser.ResolveAsync("~", TestContext.Current.CancellationToken);

        probe.CanonicalPath.ShouldBe(home.TrimEnd(Path.DirectorySeparatorChar));
    }
}
