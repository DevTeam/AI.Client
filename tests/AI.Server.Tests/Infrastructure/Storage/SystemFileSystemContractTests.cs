namespace AI.Infrastructure.Tests.Storage;

using AI.Contracts.FileSystem;
using Shouldly;
using Xunit;

/// <summary>
/// The same contract, asserted against the platform adapter, plus the behaviour only the platform can
/// show: a link that is really followed, and a document replaced while somebody holds it open.
/// Tagged <c>Category=Integration</c> because it creates real directories and links — see
/// <see href="docs/decisions/ADR-013-file-system-contract.md">ADR-013</see>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SystemFileSystemContractTests : IDisposable
{
    private readonly ContractEnvironment _environment = ContractEnvironment.OnDisk();

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
    /// The catching test for the containment risk the audit names (§7.1 #3): a junction planted inside
    /// a granted root points outside it, and a canonicalization that follows no link leaves it looking
    /// like an ordinary child of the grant. Without this the member could quietly regress to path
    /// algebra and only a security review would notice.
    /// </summary>
    [Fact]
    public async Task ResolvingAPathThroughAJunctionShouldNameThePlaceTheBytesLive()
    {
        if (!TryCreateJunction(out var reason)) Assert.Skip(reason);

        // Given a directory outside the root, reached through a junction inside it,
        var granted = Path.Combine(_environment.Root, "granted");
        var outside = Path.Combine(_environment.Root, "outside");
        Directory.CreateDirectory(granted);
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "secret.txt"), "content",
            TestContext.Current.CancellationToken);
        var junction = Path.Combine(granted, "escape");
        CreateJunction(junction, outside);
        try
        {
            // When a path through the junction is resolved,
            var resolved = await _environment.Files.ResolveLinkTargetAsync(Path.Combine(junction, "secret.txt"),
                TestContext.Current.CancellationToken);

            // Then the answer names where the bytes actually are — the junction is followed. Canonicalizing
            // the text alone would have answered with a path that merely looks like a child of the grant.
            resolved.ShouldBe(Path.GetFullPath(Path.Combine(outside, "secret.txt")), StringCompareShould.IgnoreCase);

            // And containment refuses it, which is the whole point of resolving: the escape is now visible.
            new SystemPath().IsInside(resolved, granted, recursive: true).ShouldBeFalse();

            // A path below the junction that does not exist is still answered with where it would land, so a
            // file about to be created under a junction is judged outside the grant too.
            var absent = await _environment.Files.ResolveLinkTargetAsync(Path.Combine(junction, "new.txt"),
                TestContext.Current.CancellationToken);
            absent.ShouldBe(Path.GetFullPath(Path.Combine(outside, "new.txt")), StringCompareShould.IgnoreCase);
            new SystemPath().IsInside(absent, granted, recursive: true).ShouldBeFalse();
        }
        finally
        {
            // Remove the junction before the environment deletes its tree: a recursive delete refuses to
            // walk through a reparse point, so leaving it would fail this test's own cleanup. Deleting the
            // link non-recursively removes the reparse point and leaves the target outside it alone.
            if (Directory.Exists(junction) || File.Exists(junction)) Directory.Delete(junction, recursive: false);
        }
    }

    /// <summary>
    /// The catching test for the save-under-a-reader risk the audit names (§7.1 #1): a document that
    /// somebody holds open is still replaced, which on Windows is the difference between
    /// <see cref="File.Replace(string, string, string?)"/> and a plain rename.
    /// </summary>
    [Fact]
    public async Task ASaveShouldLandWhileSomebodyHoldsTheDocumentOpen()
    {
        var token = TestContext.Current.CancellationToken;
        var source = Path.Combine(_environment.Root, "chat.json.tmp");
        var destination = Path.Combine(_environment.Root, "chat.json");
        await File.WriteAllTextAsync(source, "new", token);
        await File.WriteAllTextAsync(destination, "old", token);

        // Given a reader that holds the destination open the way this contract's own reader does.
        // The sharing has to be the contract's default rather than a narrower one chosen here: the
        // default includes delete, and on Windows share-delete is precisely what lets a save replace
        // the document underneath the handle. A handle that refuses delete would make this assertion
        // unsatisfiable on the platform it exists to protect, which is not the behaviour under test.
        await using var reader = await _environment.Files.OpenReadAsync(destination, token);
        reader.ShouldNotBeNull();

        // When the save lands,
        await _environment.Files.MoveAsync(source, destination, overwrite: true, token);

        // Then it succeeded rather than failing with an access denial naming a directory whose
        // permissions were never at fault.
        (await _environment.Files.ReadTextAsync(destination, token)).ShouldBe("new");
    }

    /// <summary>
    /// Whether this account and platform can create a junction, decided by the creation alone.
    /// </summary>
    /// <remarks>
    /// Cleanup deliberately sits outside the classification. A recursive delete refuses to walk through
    /// a reparse point, so cleaning a probe that contains a junction throws; when that throw was caught
    /// here it was reported as "this account cannot create a junction", and the catching test skipped on
    /// every machine — including the one where the adapter's link following was never verified at all.
    /// Removing the link non-recursively first removes the reparse point and not the target, so the probe
    /// can be torn down. A failure to tear it down is a real defect and must fail the test loudly.
    /// </remarks>
    private static bool TryCreateJunction(out string reason)
    {
        var probe = Directory.CreateTempSubdirectory("ai-client-junction-probe");
        var target = Path.Combine(probe.FullName, "target");
        var link = Path.Combine(probe.FullName, "link");
        try
        {
            Directory.CreateDirectory(target);
            CreateJunction(link, target);
            if (!Directory.Exists(link))
            {
                reason = "This platform created no link to test with.";
                return false;
            }
        }
        catch (Exception error)
            when (error is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            reason = $"This account cannot create a link or a junction: {error.Message}";
            return false;
        }
        finally
        {
            if (Directory.Exists(link) || File.Exists(link)) Directory.Delete(link, recursive: false);
            probe.Delete(true);
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// A junction rather than a symbolic link: creating one needs no elevation on Windows, so the test
    /// runs for an ordinary developer account instead of skipping on the platform it matters on.
    /// </summary>
    private static void CreateJunction(string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = System.Diagnostics.Process.Start(start)
                ?? throw new IOException("cmd.exe could not be started to create a junction.");
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new IOException(
                    $"mklink /J failed: {process.StandardError.ReadToEnd()}{process.StandardOutput.ReadToEnd()}");
        }
        else Directory.CreateSymbolicLink(link, target);
    }
}
