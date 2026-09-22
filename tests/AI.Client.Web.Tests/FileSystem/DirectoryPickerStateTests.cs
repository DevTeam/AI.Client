namespace AI.Client.Web.Tests.FileSystem;

using AI.Client.Contracts.FileSystem;
using AI.Client.Web.FileSystem;
using Moq;
using Shouldly;
using Xunit;

// Paths here are written in the POSIX spelling. The picker never parses one — it passes whatever
// the host hands back straight to the next call — so the separator is the host's business, not
// this class's, and these tests stay readable on either.
public class DirectoryPickerStateTests
{
    private static readonly DirectoryListing Roots = new(
        string.Empty, null, true, [new DirectoryEntry("/", "/")], []);

    private static readonly DirectoryListing Projects = new(
        "/projects", "/", true,
        [new DirectoryEntry("Alpha", "/projects/Alpha"), new DirectoryEntry("beta", "/projects/beta")],
        []);

    private static readonly DirectoryListing Alpha = new(
        "/projects/Alpha", "/projects", true, [],
        [new DirectoryEntry("one.txt", "/projects/Alpha/one.txt"), new DirectoryEntry("two.md", "/projects/Alpha/two.md")]);

    private readonly Mock<IFileSystemApi> _api = new(MockBehavior.Strict);

    private DirectoryPickerState CreateState() => new(_api.Object);

    private void SetupRoots() => _api
        .Setup(api => api.ListRootsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(Roots);

    private void SetupListing(string path, DirectoryListing? listing) => _api
        .Setup(api => api.ListAsync(path, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(listing);

    [Fact]
    public async Task OpensAtTheRememberedDirectory()
    {
        SetupListing("/projects", Projects);
        var state = CreateState();

        await state.OpenAsync("/projects", DirectoryPickerMode.Directory, TestContext.Current.CancellationToken);

        state.Listing.ShouldBe(Projects);
        state.PathText.ShouldBe("/projects");
        state.Selection.ShouldBe("/projects");
    }

    // A remembered directory outlives the directory itself. Falling back to the roots is where the
    // picker would have opened with nothing remembered, so it is not worth an error.
    [Fact]
    public async Task FallsBackToTheRootsWhenTheRememberedDirectoryIsGone()
    {
        SetupListing("/gone", null);
        SetupRoots();
        var state = CreateState();

        await state.OpenAsync("/gone", DirectoryPickerMode.Directory, TestContext.Current.CancellationToken);

        state.Listing.ShouldBe(Roots);
        state.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task OffersNothingToSelectAtTheRootsLevel()
    {
        SetupRoots();
        var state = CreateState();

        await state.OpenAsync(null, DirectoryPickerMode.Directory, TestContext.Current.CancellationToken);

        state.Selection.ShouldBeNull();
        state.CanGoUp.ShouldBeFalse();
    }

    [Fact]
    public async Task ReportsThatTheHostDoesNotOfferBrowsing()
    {
        _api.Setup(api => api.ListRootsAsync(It.IsAny<CancellationToken>())).ReturnsAsync((DirectoryListing?)null);
        var state = CreateState();

        await state.OpenAsync(null, DirectoryPickerMode.Directory, TestContext.Current.CancellationToken);

        state.IsUnavailable.ShouldBeTrue();
        state.Listing.ShouldBeNull();
    }

    [Fact]
    public async Task GoingUpFromARootReachesTheRootsLevel()
    {
        SetupListing("/projects", Projects);
        SetupListing("/", new DirectoryListing("/", string.Empty, true, [], []));
        SetupRoots();
        var state = CreateState();
        await state.OpenAsync("/projects", DirectoryPickerMode.Directory, TestContext.Current.CancellationToken);

        await state.GoUpAsync(TestContext.Current.CancellationToken);
        await state.GoUpAsync(TestContext.Current.CancellationToken);

        state.Listing.ShouldBe(Roots);
    }

    [Fact]
    public async Task FiltersTheListingWithoutRegardToCase()
    {
        SetupListing("/projects", Projects);
        var state = CreateState();
        await state.OpenAsync("/projects", DirectoryPickerMode.Directory, TestContext.Current.CancellationToken);

        state.SetFilter("B");

        state.VisibleDirectories.ShouldHaveSingleItem().Name.ShouldBe("beta");
    }

    [Fact]
    public async Task ClearsTheFilterOnNavigationSoTheNextFolderIsNotSilentlyEmpty()
    {
        SetupListing("/projects", Projects);
        SetupListing("/projects/Alpha", new DirectoryListing("/projects/Alpha", "/projects", true, [], []));
        var state = CreateState();
        await state.OpenAsync("/projects", DirectoryPickerMode.Directory, TestContext.Current.CancellationToken);
        state.SetFilter("zzz");

        await state.NavigateAsync("/projects/Alpha", TestContext.Current.CancellationToken);

        state.Filter.ShouldBeEmpty();
    }

    // A grant may legitimately name a directory that is not there yet — a repository cloned after
    // the grant is made. The picker says so and still lets it through.
    [Fact]
    public async Task OffersATypedPathThatDoesNotExistYet()
    {
        SetupRoots();
        SetupListing("/later", null);
        _api.Setup(api => api.ResolveAsync("/later", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryProbe("/later", true, false, false));
        var state = CreateState();
        await state.OpenAsync(null, DirectoryPickerMode.Directory, TestContext.Current.CancellationToken);

        state.SetPathText("/later");
        await state.GoToTypedPathAsync(TestContext.Current.CancellationToken);

        state.Selection.ShouldBe("/later");
        state.WarningMessage.ShouldNotBeNull();
        state.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task DropsTheOfferOfAMissingPathOnceNavigationResumes()
    {
        SetupRoots();
        SetupListing("/later", null);
        SetupListing("/projects", Projects);
        _api.Setup(api => api.ResolveAsync("/later", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryProbe("/later", true, false, false));
        var state = CreateState();
        await state.OpenAsync(null, DirectoryPickerMode.Directory, TestContext.Current.CancellationToken);
        state.SetPathText("/later");
        await state.GoToTypedPathAsync(TestContext.Current.CancellationToken);

        await state.NavigateAsync("/projects", TestContext.Current.CancellationToken);

        state.Selection.ShouldBe("/projects");
        state.WarningMessage.ShouldBeNull();
    }

    // The directory is there; it just will not open. Calling that "not found" would send the user
    // hunting for a folder they are looking straight at, so it stays selectable.
    [Fact]
    public async Task SaysSoWhenADirectoryRefusesToOpenButStillLetsItBeGranted()
    {
        SetupListing("/locked", new DirectoryListing("/locked", "/", false, [], []));
        var state = CreateState();

        await state.OpenAsync("/locked", DirectoryPickerMode.Directory, TestContext.Current.CancellationToken);

        state.WarningMessage.ShouldNotBeNull();
        state.ErrorMessage.ShouldBeNull();
        state.Selection.ShouldBe("/locked");
    }

    // Enter in the path box has two jobs. Right after navigating, the box names where you already
    // are, so there is nowhere to go and Enter takes the folder.
    [Fact]
    public async Task TreatsAnUntouchedPathBoxAsNamingTheSelection()
    {
        SetupListing("/projects", Projects);
        var state = CreateState();

        await state.OpenAsync("/projects", DirectoryPickerMode.Directory, TestContext.Current.CancellationToken);

        state.PathTextNamesSelection.ShouldBeTrue();
    }

    [Fact]
    public async Task DoesNotTreatAPathTypedOverTheBoxAsTheSelection()
    {
        SetupListing("/projects", Projects);
        var state = CreateState();
        await state.OpenAsync("/projects", DirectoryPickerMode.Directory, TestContext.Current.CancellationToken);

        state.SetPathText("/projects/Alpha");

        state.PathTextNamesSelection.ShouldBeFalse();
    }

    // Otherwise a path that is not there yet could never be taken: Enter would keep resolving it
    // to the same warning instead of ever selecting it.
    [Fact]
    public async Task TreatsTheBoxAsNamingAMissingPathItJustOffered()
    {
        SetupRoots();
        SetupListing("/later", null);
        _api.Setup(api => api.ResolveAsync("/later", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryProbe("/later", true, false, false));
        var state = CreateState();
        await state.OpenAsync(null, DirectoryPickerMode.Directory, TestContext.Current.CancellationToken);
        state.SetPathText("/later");

        await state.GoToTypedPathAsync(TestContext.Current.CancellationToken);

        state.PathTextNamesSelection.ShouldBeTrue();
    }

    [Fact]
    public async Task NamesNoSelectionAtTheRootsLevelWhereTheBoxIsEmpty()
    {
        SetupRoots();
        var state = CreateState();

        await state.OpenAsync(null, DirectoryPickerMode.Directory, TestContext.Current.CancellationToken);

        state.PathTextNamesSelection.ShouldBeFalse();
    }

    // File mode -----------------------------------------------------------------------------

    // Standing in a folder is an answer when a directory was asked for, and is not one when a file
    // was: there, the folder is only how you got to the file.
    [Fact]
    public async Task OffersNothingUntilAFileIsPickedInFileMode()
    {
        SetupListing("/projects/Alpha", Alpha);
        var state = CreateState();

        await state.OpenAsync("/projects/Alpha", DirectoryPickerMode.File, TestContext.Current.CancellationToken);

        state.Selection.ShouldBeNull();
        state.VisibleFiles.Select(item => item.Name).ShouldBe(["one.txt", "two.md"]);
    }

    [Fact]
    public async Task TakesTheFileThatWasClicked()
    {
        SetupListing("/projects/Alpha", Alpha);
        var state = CreateState();
        await state.OpenAsync("/projects/Alpha", DirectoryPickerMode.File, TestContext.Current.CancellationToken);

        state.SelectFile("/projects/Alpha/two.md");

        state.Selection.ShouldBe("/projects/Alpha/two.md");
        state.PathText.ShouldBe("/projects/Alpha/two.md");
    }

    [Fact]
    public async Task DropsThePickedFileOnceTheFolderChanges()
    {
        SetupListing("/projects/Alpha", Alpha);
        SetupListing("/projects", Projects);
        var state = CreateState();
        await state.OpenAsync("/projects/Alpha", DirectoryPickerMode.File, TestContext.Current.CancellationToken);
        state.SelectFile("/projects/Alpha/one.txt");

        await state.GoUpAsync(TestContext.Current.CancellationToken);

        state.SelectedFile.ShouldBeNull();
        state.Selection.ShouldBeNull();
    }

    // Asking for files is what makes them arrive: a directory-mode listing must not pay for them.
    [Fact]
    public async Task AsksForFilesOnlyInFileMode()
    {
        SetupListing("/projects/Alpha", Alpha);
        var state = CreateState();

        await state.OpenAsync("/projects/Alpha", DirectoryPickerMode.Directory, TestContext.Current.CancellationToken);

        state.VisibleFiles.ShouldBeEmpty();
        _api.Verify(api => api.ListAsync("/projects/Alpha", false, It.IsAny<CancellationToken>()));
    }

    // A pasted file path should show where it lives, not just sit in the box: the folder around it
    // opens and the file comes back picked.
    [Fact]
    public async Task OpensTheFolderAroundATypedFileAndPicksIt()
    {
        SetupRoots();
        SetupListing("/projects/Alpha", Alpha);
        SetupListing("/projects/Alpha/two.md", null);
        _api.Setup(api => api.ResolveAsync("/projects/Alpha/two.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryProbe("/projects/Alpha/two.md", true, false, true));
        var state = CreateState();
        await state.OpenAsync(null, DirectoryPickerMode.File, TestContext.Current.CancellationToken);

        state.SetPathText("/projects/Alpha/two.md");
        await state.GoToTypedPathAsync(TestContext.Current.CancellationToken);

        state.Listing!.CurrentPath.ShouldBe("/projects/Alpha");
        state.Selection.ShouldBe("/projects/Alpha/two.md");
    }

    [Fact]
    public async Task ReopensAtTheFolderOfTheFileItWasGiven()
    {
        SetupListing("/projects/Alpha", Alpha);
        SetupListing("/projects/Alpha/one.txt", null);
        _api.Setup(api => api.ResolveAsync("/projects/Alpha/one.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryProbe("/projects/Alpha/one.txt", true, false, true));
        var state = CreateState();

        await state.OpenAsync("/projects/Alpha/one.txt", DirectoryPickerMode.File, TestContext.Current.CancellationToken);

        state.Listing!.CurrentPath.ShouldBe("/projects/Alpha");
        state.Selection.ShouldBe("/projects/Alpha/one.txt");
    }

    // Handing back a file where a directory was asked for would be answering a different question.
    [Fact]
    public async Task RefusesAFileWhenADirectoryWasAskedFor()
    {
        SetupRoots();
        SetupListing("/projects/Alpha/two.md", null);
        _api.Setup(api => api.ResolveAsync("/projects/Alpha/two.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryProbe("/projects/Alpha/two.md", true, false, true));
        var state = CreateState();
        await state.OpenAsync(null, DirectoryPickerMode.Directory, TestContext.Current.CancellationToken);

        state.SetPathText("/projects/Alpha/two.md");
        await state.GoToTypedPathAsync(TestContext.Current.CancellationToken);

        state.ErrorMessage.ShouldNotBeNull();
        state.Selection.ShouldBeNull();
    }

    // Naming a file that is about to be written is a real answer, same as granting a directory that
    // does not exist yet.
    [Fact]
    public async Task OffersAFileThatIsNotThereYet()
    {
        SetupRoots();
        SetupListing("/projects/Alpha/new.txt", null);
        _api.Setup(api => api.ResolveAsync("/projects/Alpha/new.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryProbe("/projects/Alpha/new.txt", true, false, false));
        var state = CreateState();
        await state.OpenAsync(null, DirectoryPickerMode.File, TestContext.Current.CancellationToken);

        state.SetPathText("/projects/Alpha/new.txt");
        await state.GoToTypedPathAsync(TestContext.Current.CancellationToken);

        state.Selection.ShouldBe("/projects/Alpha/new.txt");
        state.WarningMessage.ShouldNotBeNull();
    }

    // The tool server drops a grant whose root is relative without a word, so the picker has to
    // refuse the path while the person who typed it is still looking at it.
    [Fact]
    public async Task RefusesATypedPathThatNamesNoRoot()
    {
        SetupRoots();
        SetupListing("some/relative/path", null);
        _api.Setup(api => api.ResolveAsync("some/relative/path", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryProbe("some/relative/path", false, false, false));
        var state = CreateState();
        await state.OpenAsync(null, DirectoryPickerMode.Directory, TestContext.Current.CancellationToken);

        state.SetPathText("some/relative/path");
        await state.GoToTypedPathAsync(TestContext.Current.CancellationToken);

        state.ErrorMessage.ShouldNotBeNull();
        state.Selection.ShouldBeNull();
    }

    [Fact]
    public async Task KeepsTheCurrentListingWhenADirectoryCannotBeOpenedAtAll()
    {
        SetupListing("/projects", Projects);
        SetupListing("/projects/Alpha", null);
        var state = CreateState();
        await state.OpenAsync("/projects", DirectoryPickerMode.Directory, TestContext.Current.CancellationToken);

        await state.NavigateAsync("/projects/Alpha", TestContext.Current.CancellationToken);

        state.ErrorMessage.ShouldNotBeNull();
        state.Listing.ShouldBe(Projects);
    }
}
