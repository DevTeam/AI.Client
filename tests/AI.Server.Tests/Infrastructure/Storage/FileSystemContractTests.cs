namespace AI.Infrastructure.Tests.Storage;

using AI.Contracts.FileSystem;
using Shouldly;
using Xunit;

/// <summary>
/// The contract, asserted against the in-memory implementation. Fast and part of the default suite:
/// every consumer of <see cref="AI.Contracts.FileSystem.IFileSystem"/> is written against these
/// expectations, so they are what the rest of the tests may assume about a fake.
/// </summary>
public sealed class FileSystemContractTests : IDisposable
{
    private readonly ContractEnvironment _environment = ContractEnvironment.InMemory();

    public void Dispose() => _environment.Dispose();

    [Fact]
    public Task AbsenceShouldBeAnAnswerRatherThanAnError() =>
        FileSystemContractAssertions.AbsenceIsAnAnswerRatherThanAnError(_environment);

    [Fact]
    public Task AWriteShouldCreateTheParentAndReadBackUnchanged() =>
        FileSystemContractAssertions.AWriteCreatesTheParentAndReadsBackUnchanged(_environment);

    [Fact]
    public Task LinesShouldSplitOnNewlineAndDropTheCarriageReturn() =>
        FileSystemContractAssertions.LinesSplitOnNewlineAndDropTheCarriageReturn(_environment);

    [Fact]
    public Task AppendingAndStreamingShouldBothLandTheContent() =>
        FileSystemContractAssertions.AppendingAddsToTheEndAndAnOpenWriteLandsItsContent(_environment);

    [Fact]
    public Task AReadShouldAnswerTheSameWayWithAndWithoutOptions() =>
        FileSystemContractAssertions.AReadWithOptionsAnswersWhatAReadWithoutThemAnswers(_environment);

    [Fact]
    public Task DeletingAFileShouldRefuseADirectory() =>
        FileSystemContractAssertions.DeletingAFileRefusesADirectory(_environment);

    [Fact]
    public Task DeletingADirectoryShouldObeyTheRecursiveFlag() =>
        FileSystemContractAssertions.DeletingADirectoryObeysTheRecursiveFlag(_environment);

    [Fact]
    public Task MovingShouldRefuseToOverwriteByDefaultAndKeepTheSource() =>
        FileSystemContractAssertions.MovingRefusesToOverwriteByDefaultAndKeepsTheSource(_environment);

    [Fact]
    public Task MovingShouldReplaceWhenAskedTo() =>
        FileSystemContractAssertions.MovingReplacesWhenAskedTo(_environment);

    [Fact]
    public Task MovingADirectoryShouldTakeItsContentsAndRefuseAnExistingDestination() =>
        FileSystemContractAssertions.MovingADirectoryTakesItsContentsAndRefusesAnExistingDestination(_environment);

    [Fact]
    public Task ListingShouldRecurseAndFilterOnlyWhenAsked() =>
        FileSystemContractAssertions.ListingRecursesAndFiltersOnlyWhenAsked(_environment);

    [Fact]
    public Task ResolvingAPathShouldFollowItToTheSamePlaceEveryTime() =>
        FileSystemContractAssertions.ResolvingAPathFollowsItToTheSamePlaceEveryTime(_environment);

    /// <summary>
    /// The one expectation the fake cannot meet, stated as a test so the gap is visible rather than
    /// remembered: a link of the host system is modelled by neither the fake nor its path algebra,
    /// which is why the containment test that plants a junction lives in the adapter test below.
    /// </summary>
    [Fact]
    public async Task TheFakeShouldNotClaimToModelALinkOfTheHostSystem()
    {
        // Given a path that only exists as the fake's own convention, with no link anywhere,
        var path = _environment.Path.Combine(_environment.Path.Combine(_environment.Root, "absent"), "file.txt");

        // When it is resolved,
        var resolved = await _environment.Files.ResolveLinkTargetAsync(path, TestContext.Current.CancellationToken);

        // Then the answer is the canonical path, not a promise about a link: the fake models no link,
        // and a test needing one has to run against SystemFileSystem.
        resolved.ShouldBe(_environment.Path.GetFullPath(path), StringCompareShould.IgnoreCase);
    }

    /// <summary>
    /// The catching test for the drive-root defect: a path directly under a drive root has the root
    /// as its directory, not the drive-relative <c>C:</c> that names nothing. A fake path algebra
    /// that answered <c>C:</c> sent the upward walk of a write past the root and crashed the next
    /// normalization, which is why every write under a drive root failed at once.
    /// </summary>
    [Fact]
    public void APathDirectlyUnderADriveRootShouldAnswerTheRootAsItsDirectory()
    {
        // Given the fake's own default root, whose parent is the drive root,
        var path = new InMemoryPath(PathSemantics.Windows, @"C:\data");

        // Then the segment above a path directly under the root is the root itself,
        path.GetDirectoryName(@"C:\data").ShouldBe(@"C:\");
        path.GetDirectoryName(@"C:\data\chat.json").ShouldBe(@"C:\data");

        // And the walk ends there: the root has nothing above it, so it terminates by itself rather
        // than walking on forever into a drive-relative path nothing can open.
        path.GetDirectoryName(@"C:\").ShouldBeNull();
        path.GetDirectoryName(path.GetPathRoot(@"C:\data")!).ShouldBeNull();
    }

    /// <summary>
    /// The same defect seen from the caller: a write whose path sits directly under a drive root has
    /// to succeed and be listed. This is the shape the 11 contract cases and the skill case took,
    /// because every one of them wrote under a drive root through <see cref="MemoryFileSystem"/>.
    /// </summary>
    [Fact]
    public async Task AWriteUnderADriveRootShouldLandAndBeListed()
    {
        var token = TestContext.Current.CancellationToken;
        var path = new InMemoryPath(PathSemantics.Windows, @"C:\data");
        var files = new MemoryFileSystem(path);

        // When a document directly under the drive root is written,
        await files.WriteTextAsync(@"C:\fresh\chat.json", "content", token);

        // Then it landed, its parent exists, and a listing of the root reports both the directory
        // and the document rather than throwing on the walk that creates the parent.
        (await files.ReadTextAsync(@"C:\fresh\chat.json", token)).ShouldBe("content");
        (await files.DirectoryExistsAsync(@"C:\fresh", token)).ShouldBeTrue();
        files.Directories.ShouldContain(@"C:\");
        (await files.ListEntriesAsync(@"C:\", new FileEnumerationOptions(Recursive: true), token))
            .Select(entry => entry.Name).Order(StringComparer.Ordinal).ShouldBe(["chat.json", "fresh"]);
    }
}
