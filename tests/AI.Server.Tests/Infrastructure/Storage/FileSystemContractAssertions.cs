namespace AI.Infrastructure.Tests.Storage;

using System.Text;
using AI.Contracts.FileSystem;
using Shouldly;
using Xunit;

/// <summary>
/// One set of expectations, applied to both implementations of <see cref="IFileSystem"/>.
/// </summary>
/// <remarks>
/// A fake that disagrees with the platform makes every test written against it a lie, and the
/// disagreement is never visible from inside a single implementation — which is why the assertions
/// live once, here, and the two test classes run the same list against <see cref="MemoryFileSystem"/>
/// and <see cref="SystemFileSystem"/>.
/// What the fake genuinely cannot model — a real link, a reader holding a file open, a second process —
/// is not claimed here for the fake; it is asserted against the adapter and tagged
/// <c>Category=Integration</c>, as <see href="docs/decisions/ADR-013-file-system-contract.md">ADR-013</see>
/// records.
/// Paths are built through the environment's own <see cref="IPath"/>, so an assertion never depends on
/// the separator or the root of the machine the test happens to run on.
/// </remarks>
internal static class FileSystemContractAssertions
{
    public static async Task AbsenceIsAnAnswerRatherThanAnError(ContractEnvironment environment)
    {
        var files = environment.Files;
        var token = TestContext.Current.CancellationToken;
        var missing = Under(environment, environment.Root, "nothing.json");
        var deeper = Under(environment, missing, "deeper");

        (await files.FileExistsAsync(missing, token)).ShouldBeFalse();
        (await files.DirectoryExistsAsync(missing, token)).ShouldBeFalse();
        (await files.ReadTextAsync(missing, token)).ShouldBeNull();
        (await files.ReadBytesAsync(missing, token)).ShouldBeNull();
        (await files.OpenReadAsync(missing, token)).ShouldBeNull();
        (await files.GetEntryAsync(missing, token)).ShouldBeNull();
        (await files.ListFilesAsync(deeper, "*", token)).ShouldBeEmpty();
        (await files.ListEntriesAsync(deeper, new FileEnumerationOptions(), token)).ShouldBeEmpty();

        // Deleting what is already gone is the state the caller asked for, not a failure.
        await files.DeleteFileAsync(missing, token);
        await files.DeleteDirectoryAsync(deeper, recursive: true, token);
    }

    public static async Task AWriteCreatesTheParentAndReadsBackUnchanged(ContractEnvironment environment)
    {
        var files = environment.Files;
        var token = TestContext.Current.CancellationToken;
        var path = Under(environment, environment.Root, "created", "deep", "chat.json");

        await files.WriteTextAsync(path, "{\"kind\":\"тест\"}", token);

        (await files.FileExistsAsync(path, token)).ShouldBeTrue();
        (await files.DirectoryExistsAsync(Under(environment, environment.Root, "created", "deep"), token))
            .ShouldBeTrue();
        (await files.ReadTextAsync(path, token)).ShouldBe("{\"kind\":\"тест\"}");

        // UTF-8 without a BOM: a byte-order mark would travel into every document the product writes,
        // and the platform's text writers are not consistent about emitting one.
        var bytes = await files.ReadBytesAsync(path, token);
        bytes.ShouldNotBeNull();
        bytes.ShouldBe(Encoding.UTF8.GetBytes("{\"kind\":\"тест\"}"));
    }

    public static async Task LinesSplitOnNewlineAndDropTheCarriageReturn(ContractEnvironment environment)
    {
        var files = environment.Files;
        var token = TestContext.Current.CancellationToken;
        var path = Under(environment, environment.Root, "lines.txt");

        // A newline at the end terminates the last line instead of opening an empty one.
        await files.WriteTextAsync(path, "first\r\nsecond\nthird\n", token);
        (await Collect(files.ReadLinesAsync(path, token))).ShouldBe(["first", "second", "third"]);

        await files.WriteTextAsync(path, "only", token);
        (await Collect(files.ReadLinesAsync(path, token))).ShouldBe(["only"]);

        // An absent document yields nothing rather than throwing.
        (await Collect(files.ReadLinesAsync(Under(environment, environment.Root, "absent.txt"), token)))
            .ShouldBeEmpty();
    }

    public static async Task AppendingAddsToTheEndAndAnOpenWriteLandsItsContent(ContractEnvironment environment)
    {
        var files = environment.Files;
        var token = TestContext.Current.CancellationToken;
        var path = Under(environment, environment.Root, "appended.log");

        await files.AppendTextAsync(path, "first\n", token);
        await files.AppendTextAsync(path, "second\n", token);
        (await files.ReadTextAsync(path, token)).ShouldBe("first\nsecond\n");

        var streamed = Under(environment, environment.Root, "streamed.bin");
        await using (var stream = await files.OpenWriteAsync(streamed, token))
        {
            await stream.WriteAsync("payload"u8.ToArray(), token);
        }

        (await files.ReadTextAsync(streamed, token)).ShouldBe("payload");
    }

    /// <summary>
    /// A read that names its own options answers exactly what a read without them answers. The options
    /// describe how the document is opened — the sharing the open allows, the buffer it reads through,
    /// the platform hints — so an implementation that read *less* or *differently* would be a silent
    /// wrong answer rather than a different convenience.
    /// </summary>
    public static async Task AReadWithOptionsAnswersWhatAReadWithoutThemAnswers(ContractEnvironment environment)
    {
        var files = environment.Files;
        var token = TestContext.Current.CancellationToken;
        var path = Under(environment, environment.Root, "options.txt");
        const string content = "first\nsecond\nthird";
        await files.WriteTextAsync(path, content, token);

        // The narrowest sharing and a deliberately small buffer: the answer may not depend on either.
        var narrow = new FileReadOptions(Share: FileShare.Read, BufferSize: 16, Options: FileOptions.SequentialScan);
        (await files.ReadTextAsync(path, narrow, token)).ShouldBe(content);
        (await files.ReadTextAsync(path, FileReadOptions.Default, token)).ShouldBe(content);

        var bytes = await files.ReadBytesAsync(path, narrow, token);
        bytes.ShouldNotBeNull();
        bytes.ShouldBe(Encoding.UTF8.GetBytes(content));

        await using (var stream = await files.OpenReadAsync(path, narrow, token))
        {
            stream.ShouldNotBeNull();
            using var reader = new StreamReader(stream);
            (await reader.ReadToEndAsync(token)).ShouldBe(content);
        }

        (await Collect(files.ReadLinesAsync(path, narrow, token))).ShouldBe(["first", "second", "third"]);

        // Absence stays absence with options as well: the sharing a caller asked for does not turn a
        // document that is not there into a failure.
        (await files.ReadTextAsync(Under(environment, environment.Root, "none.txt"), narrow, token)).ShouldBeNull();
        (await files.OpenReadAsync(Under(environment, environment.Root, "none.txt"), narrow, token)).ShouldBeNull();
    }

    public static async Task DeletingAFileRefusesADirectory(ContractEnvironment environment)
    {
        var files = environment.Files;
        var token = TestContext.Current.CancellationToken;
        var directory = Under(environment, environment.Root, "a-directory");
        await files.CreateDirectoryAsync(directory, ownerOnly: false, token);

        // The exception type is deliberately not pinned: the platform and the fake describe "that is a
        // directory, not a file" differently, and what a caller may rely on is that it is refused and
        // that the directory is still there afterwards.
        await Should.ThrowAsync<Exception>(() => files.DeleteFileAsync(directory, token));
        (await files.DirectoryExistsAsync(directory, token)).ShouldBeTrue();
    }

    public static async Task DeletingADirectoryObeysTheRecursiveFlag(ContractEnvironment environment)
    {
        var files = environment.Files;
        var token = TestContext.Current.CancellationToken;
        var root = Under(environment, environment.Root, "tree");
        var inner = Under(environment, root, "inner", "file.txt");
        await files.WriteTextAsync(inner, "content", token);

        await Should.ThrowAsync<Exception>(() => files.DeleteDirectoryAsync(root, recursive: false, token));
        (await files.FileExistsAsync(inner, token)).ShouldBeTrue();

        await files.DeleteDirectoryAsync(root, recursive: true, token);
        (await files.DirectoryExistsAsync(root, token)).ShouldBeFalse();
        (await files.FileExistsAsync(inner, token)).ShouldBeFalse();
    }

    public static async Task MovingRefusesToOverwriteByDefaultAndKeepsTheSource(ContractEnvironment environment)
    {
        var files = environment.Files;
        var token = TestContext.Current.CancellationToken;
        var source = Under(environment, environment.Root, "move", "source.txt");
        var destination = Under(environment, environment.Root, "move", "destination.txt");
        await files.WriteTextAsync(source, "source", token);
        await files.WriteTextAsync(destination, "destination", token);

        await Should.ThrowAsync<IOException>(() => files.MoveAsync(source, destination, overwrite: false, token));

        // A refused move that had already taken the source away would be worse than the refusal.
        (await files.ReadTextAsync(source, token)).ShouldBe("source");
        (await files.ReadTextAsync(destination, token)).ShouldBe("destination");
    }

    public static async Task MovingReplacesWhenAskedTo(ContractEnvironment environment)
    {
        var files = environment.Files;
        var token = TestContext.Current.CancellationToken;
        var source = Under(environment, environment.Root, "replace", "new.json");
        var destination = Under(environment, environment.Root, "replace", "chat.json");
        await files.WriteTextAsync(source, "new", token);
        await files.WriteTextAsync(destination, "old", token);

        await files.MoveAsync(source, destination, overwrite: true, token);

        (await files.ReadTextAsync(destination, token)).ShouldBe("new");
        (await files.FileExistsAsync(source, token)).ShouldBeFalse();
    }

    public static async Task MovingADirectoryTakesItsContentsAndRefusesAnExistingDestination(
        ContractEnvironment environment)
    {
        var files = environment.Files;
        var token = TestContext.Current.CancellationToken;
        var source = Under(environment, environment.Root, "from");
        var destination = Under(environment, environment.Root, "to", "from");
        await files.WriteTextAsync(Under(environment, source, "nested", "file.txt"), "content", token);
        await files.CreateDirectoryAsync(Under(environment, environment.Root, "to"), ownerOnly: false, token);

        await files.MoveDirectoryAsync(source, destination, token);

        (await files.ReadTextAsync(Under(environment, destination, "nested", "file.txt"), token))
            .ShouldBe("content");
        (await files.DirectoryExistsAsync(source, token)).ShouldBeFalse();

        // A move that silently merged two directories would lose which entry came from where, so a
        // destination that already exists is refused rather than merged. The destination has to be
        // created first: a move onto a path that does not exist is a successful move, not a refusal.
        await files.CreateDirectoryAsync(Under(environment, environment.Root, "elsewhere"), ownerOnly: false, token);
        await Should.ThrowAsync<IOException>(() =>
            files.MoveDirectoryAsync(destination, Under(environment, environment.Root, "elsewhere"), token));
        await Should.ThrowAsync<DirectoryNotFoundException>(() =>
            files.MoveDirectoryAsync(Under(environment, environment.Root, "absent"), destination, token));
    }

    public static async Task ListingRecursesAndFiltersOnlyWhenAsked(ContractEnvironment environment)
    {
        var files = environment.Files;
        var token = TestContext.Current.CancellationToken;
        var root = Under(environment, environment.Root, "listing");
        await files.WriteTextAsync(Under(environment, root, "top.txt"), "a", token);
        await files.WriteTextAsync(Under(environment, root, "inner", "nested.md"), "b", token);
        await files.WriteTextAsync(Under(environment, root, "inner", "other.txt"), "c", token);

        // One level by default: a listing that quietly descends is a listing that quietly finds too much.
        // A directory is an entry like any other, so the subdirectory is named in both walks.
        Names(await files.ListEntriesAsync(root, new FileEnumerationOptions(), token))
            .ShouldBe(["inner", "top.txt"]);
        Names(await files.ListEntriesAsync(root, new FileEnumerationOptions(Recursive: true), token))
            .ShouldBe(["inner", "nested.md", "other.txt", "top.txt"]);
        Names(await files.ListEntriesAsync(root,
                new FileEnumerationOptions(Recursive: true, SearchPattern: "*.md"), token))
            .ShouldBe(["nested.md"]);

        var entries = await files.ListEntriesAsync(root, new FileEnumerationOptions(Recursive: true), token);
        var inner = entries.Single(entry => entry.Name == "inner");
        inner.IsDirectory.ShouldBeTrue();
        inner.Length.ShouldBe(0);
        entries.Single(entry => entry.Name == "top.txt").IsDirectory.ShouldBeFalse();

        (await files.ListFilesAsync(root, "*.txt", token)).Count.ShouldBe(1);
        (await files.ListFilesRecursivelyAsync(root, "*.txt", token)).Count.ShouldBe(2);
    }

    public static async Task ResolvingAPathFollowsItToTheSamePlaceEveryTime(ContractEnvironment environment)
    {
        var files = environment.Files;
        var token = TestContext.Current.CancellationToken;
        var root = Under(environment, environment.Root, "containment");
        var existing = Under(environment, root, "existing.txt");
        await files.WriteTextAsync(existing, "content", token);

        // Nothing has been planted here, so this is about the shape of the answer: a resolved root is a
        // canonical path, and resolving it again changes nothing, which is what makes it safe for a
        // containment check to ask twice. The assertions below deliberately compare the resolved root
        // with the resolved children rather than with the path that went in: where the host itself
        // reaches the temporary directory through a link, the answer is still self-consistent.
        var resolvedRoot = await files.ResolveLinkTargetAsync(root, token);
        resolvedRoot.ShouldNotBeNullOrWhiteSpace();
        (await files.ResolveLinkTargetAsync(resolvedRoot, token)).ShouldBe(resolvedRoot, StringCompareShould.IgnoreCase);

        var resolvedExisting = await files.ResolveLinkTargetAsync(existing, token);
        resolvedExisting.ShouldBe(Under(environment, resolvedRoot, "existing.txt"), StringCompareShould.IgnoreCase);
        (await files.ResolveLinkTargetAsync(resolvedExisting, token))
            .ShouldBe(resolvedExisting, StringCompareShould.IgnoreCase);

        // A path that names nothing still has to be answered with where it would land: containment is
        // asked about files that are about to be created.
        var absent = await files.ResolveLinkTargetAsync(Under(environment, root, "inner", "absent.txt"), token);
        absent.ShouldBe(Under(environment, resolvedRoot, "inner", "absent.txt"), StringCompareShould.IgnoreCase);

        // Folding '.' and '..' is part of canonicalizing: both spellings name the same file.
        (await files.ResolveLinkTargetAsync(Under(environment, root, "inner", "..", "existing.txt"), token))
            .ShouldBe(resolvedExisting, StringCompareShould.IgnoreCase);

        await Should.ThrowAsync<ArgumentException>(() => files.ResolveLinkTargetAsync("  ", token));
    }

    /// <summary>Joins path segments with the environment's own algebra, whatever it separates with.</summary>
    private static string Under(ContractEnvironment environment, string first, params string[] rest)
    {
        var combined = first;
        foreach (var segment in rest) combined = environment.Path.Combine(combined, segment);
        return combined;
    }

    /// <summary>Entry names, ordered, so a listing is compared without depending on the walk's order.</summary>
    private static string[] Names(IReadOnlyList<FileSystemEntry> entries) =>
        entries.Select(entry => entry.Name).Order(StringComparer.Ordinal).ToArray();

    private static async Task<List<string>> Collect(IAsyncEnumerable<string> lines)
    {
        var collected = new List<string>();
        await foreach (var line in lines) collected.Add(line);
        return collected;
    }
}

/// <summary>
/// The file system, path algebra and root one contract test runs against, so the same assertions apply
/// to an in-memory implementation and to the platform without either knowing about the other.
/// </summary>
internal sealed class ContractEnvironment(IFileSystem files, IPath path, string root) : IDisposable
{
    public IFileSystem Files { get; } = files;

    public IPath Path { get; } = path;

    /// <summary>The directory every path in a test is built under.</summary>
    public string Root { get; } = root;

    /// <summary>The fake, with Windows semantics chosen explicitly rather than inherited from the runner.</summary>
    public static ContractEnvironment InMemory()
    {
        var path = new InMemoryPath(PathSemantics.Windows, "C:\\data");
        return new ContractEnvironment(new MemoryFileSystem(path), path, "C:\\data");
    }

    /// <summary>The platform adapter, rooted in a real temporary directory of its own.</summary>
    public static ContractEnvironment OnDisk() => new(
        new SystemFileSystem(), new SystemPath(), Directory.CreateTempSubdirectory("ai-client-contract").FullName);

    public void Dispose()
    {
        if (!Directory.Exists(Root)) return;
        foreach (var file in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(Root, true);
    }
}
