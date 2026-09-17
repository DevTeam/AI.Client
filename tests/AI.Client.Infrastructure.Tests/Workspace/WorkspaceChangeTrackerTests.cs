namespace AI.Client.Infrastructure.Tests.Workspace;

using System.Text.Json;
using AI.Client.Application.Tools;
using AI.Client.Application.Workspace;
using AI.Client.Contracts.Tools;
using AI.Client.Contracts.Workspace;
using AI.Client.Infrastructure.Workspace;
using Shouldly;
using Xunit;

public sealed class WorkspaceChangeTrackerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aiclient-workspace").FullName;
    private readonly WorkspaceRunKey _run = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    public void Dispose() => Directory.Delete(_root, true);

    private static ToolDescriptor BuiltIn(string name) => ToolDescriptor.Basic(
        ToolRef.BuiltInPrefix + name, name, name, JsonDocument.Parse("{}").RootElement.Clone());

    private async Task<WorkspaceChangeTracker> StartAsync(params string[] roots)
    {
        var tracker = new WorkspaceChangeTracker();
        await BeginAsync(tracker, _run, null, roots);
        return tracker;
    }

    private Task BeginAsync(WorkspaceChangeTracker tracker, WorkspaceRunKey run, WorkspaceRunKey? parent,
        params string[] roots) =>
        tracker.BeginRunAsync(run,
            (roots.Length == 0 ? [_root] : roots).Select(root => new ToolDirectoryGrant(root, true, ["edit"])).ToArray(),
            parent, TestContext.Current.CancellationToken);

    private Task EditAsync(WorkspaceChangeTracker tracker, string tool, string path, Func<Task> change) =>
        EditAsync(tracker, _run, tool, path, change);

    private static async Task EditAsync(
        WorkspaceChangeTracker tracker, WorkspaceRunKey run, string tool, string path, Func<Task> change)
    {
        var descriptor = BuiltIn(tool);
        var arguments = JsonSerializer.Serialize(new { path });
        await tracker.RecordIntentAsync(run, descriptor, arguments, TestContext.Current.CancellationToken);
        await change();
        await tracker.RecordEffectAsync(run, descriptor, arguments, TestContext.Current.CancellationToken);
    }

    /// <summary>A run delegated from <see cref="_run"/>, as a subtask is.</summary>
    private WorkspaceRunKey Child() => _run with { BranchId = Guid.CreateVersion7() };

    private string Path(string name) => System.IO.Path.Combine(_root, name);

    [Fact]
    public async Task ShouldMeasureASingleEditAgainstTheFileAsItWasBeforeTheRun()
    {
        var file = Path("a.txt");
        await File.WriteAllTextAsync(file, "one\ntwo\nthree", TestContext.Current.CancellationToken);
        var tracker = await StartAsync();

        await EditAsync(tracker, "edit_file", file, () => File.WriteAllTextAsync(file, "one\nTWO\nthree"));

        var changes = await tracker.SnapshotAsync(_run, TestContext.Current.CancellationToken);
        var change = changes.Files.ShouldHaveSingleItem();
        change.Kind.ShouldBe(FileChangeKind.Modified);
        change.Additions.ShouldBe(1);
        change.Deletions.ShouldBe(1);
        changes.Additions.ShouldBe(1);
    }

    [Fact]
    public async Task ShouldCollapseRepeatedEditsOfOneFileIntoOneNetDifference()
    {
        // The headline number is what the run did, not the sum of its intermediate steps: a line
        // rewritten three times is still one changed line.
        var file = Path("a.txt");
        await File.WriteAllTextAsync(file, "one\ntwo", TestContext.Current.CancellationToken);
        var tracker = await StartAsync();

        await EditAsync(tracker, "edit_file", file, () => File.WriteAllTextAsync(file, "one\nsecond"));
        await EditAsync(tracker, "edit_file", file, () => File.WriteAllTextAsync(file, "one\nthird"));
        await EditAsync(tracker, "edit_file", file, () => File.WriteAllTextAsync(file, "one\nfourth"));

        var changes = await tracker.SnapshotAsync(_run, TestContext.Current.CancellationToken);
        changes.Files.ShouldHaveSingleItem();
        changes.Additions.ShouldBe(1);
        changes.Deletions.ShouldBe(1);
    }

    [Fact]
    public async Task ShouldNotAttributeChangesTheUserMadeBeforeTheRun()
    {
        // The baseline is captured when the run first touches the path, so whatever was already
        // different at that point belongs to the user, not to the agent.
        var file = Path("a.txt");
        await File.WriteAllTextAsync(file, "original", TestContext.Current.CancellationToken);
        var tracker = await StartAsync();
        await File.WriteAllTextAsync(file, "the user edited this\nand added a line", TestContext.Current.CancellationToken);

        await EditAsync(tracker, "edit_file", file,
            () => File.WriteAllTextAsync(file, "the user edited this\nand added a line\nagent line"));

        var change = (await tracker.SnapshotAsync(_run, TestContext.Current.CancellationToken)).Files.ShouldHaveSingleItem();
        change.Additions.ShouldBe(1);
        change.Deletions.ShouldBe(0);
    }

    [Fact]
    public async Task ShouldReportACreatedFileAsAdded()
    {
        var file = Path("new.txt");
        var tracker = await StartAsync();

        await EditAsync(tracker, "write_file", file, () => File.WriteAllTextAsync(file, "a\nb"));

        var change = (await tracker.SnapshotAsync(_run, TestContext.Current.CancellationToken)).Files.ShouldHaveSingleItem();
        change.Kind.ShouldBe(FileChangeKind.Added);
        change.Additions.ShouldBe(2);
        change.Deletions.ShouldBe(0);
    }

    [Fact]
    public async Task ShouldCarryTheContentsOfACreatedFileAsADiff()
    {
        var file = Path("new.txt");
        var tracker = await StartAsync();

        await EditAsync(tracker, "write_file", file, () => File.WriteAllTextAsync(file, "a\nb"));

        var change = (await tracker.SnapshotAsync(_run, TestContext.Current.CancellationToken)).Files.ShouldHaveSingleItem();
        var lines = UnifiedDiff.Parse(change.Diff);
        lines.Where(line => line.Kind == DiffLineKind.Added).Select(line => line.Text).ShouldBe(["a", "b"]);
    }

    [Fact]
    public async Task ShouldReportBothEndsOfAMove()
    {
        var source = Path("from.txt");
        var destination = Path("to.txt");
        await File.WriteAllTextAsync(source, "content", TestContext.Current.CancellationToken);
        var tracker = await StartAsync();

        var descriptor = BuiltIn("move_file");
        var arguments = JsonSerializer.Serialize(new { source, destination });
        await tracker.RecordIntentAsync(_run, descriptor, arguments, TestContext.Current.CancellationToken);
        File.Move(source, destination);
        await tracker.RecordEffectAsync(_run, descriptor, arguments, TestContext.Current.CancellationToken);

        var changes = await tracker.SnapshotAsync(_run, TestContext.Current.CancellationToken);
        changes.Files.Count.ShouldBe(2);
        changes.Files.ShouldContain(file => file.Kind == FileChangeKind.Deleted);
        changes.Files.ShouldContain(file => file.Kind == FileChangeKind.Added);
    }

    [Fact]
    public async Task ShouldReportADeletedFileAsDeleted()
    {
        var file = Path("gone.txt");
        await File.WriteAllTextAsync(file, "one\ntwo", TestContext.Current.CancellationToken);
        var tracker = await StartAsync();

        await EditAsync(tracker, "delete_file", file, () =>
        {
            File.Delete(file);
            return Task.CompletedTask;
        });

        var change = (await tracker.SnapshotAsync(_run, TestContext.Current.CancellationToken)).Files.ShouldHaveSingleItem();
        change.Kind.ShouldBe(FileChangeKind.Deleted);
        // The deleted lines are counted from the content the file had before the run.
        change.Deletions.ShouldBe(2);
        change.Additions.ShouldBe(0);
    }

    [Fact]
    public async Task ShouldReportAFileRemovedWithItsDirectory()
    {
        // `delete_directory` names only the directory, but a file the run had already touched is
        // compared against its own baseline and so still shows up as deleted.
        var directory = Path("sub");
        var file = System.IO.Path.Combine(directory, "gone.txt");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(file, "one\ntwo", TestContext.Current.CancellationToken);
        var tracker = await StartAsync();
        await EditAsync(tracker, "edit_file", file,
            () => File.WriteAllTextAsync(file, "one\ntwo\nthree", TestContext.Current.CancellationToken));

        var descriptor = BuiltIn("delete_directory");
        var arguments = JsonSerializer.Serialize(new { path = directory, recursive = true });
        await tracker.RecordIntentAsync(_run, descriptor, arguments, TestContext.Current.CancellationToken);
        Directory.Delete(directory, true);
        await tracker.RecordEffectAsync(_run, descriptor, arguments, TestContext.Current.CancellationToken);

        var change = (await tracker.SnapshotAsync(_run, TestContext.Current.CancellationToken)).Files.ShouldHaveSingleItem();
        change.Path.ShouldBe(file);
        change.Kind.ShouldBe(FileChangeKind.Deleted);
    }

    [Fact]
    public async Task ShouldListNothingWhenAToolTouchedAPathButChangedNothing()
    {
        var file = Path("a.txt");
        await File.WriteAllTextAsync(file, "unchanged", TestContext.Current.CancellationToken);
        var tracker = await StartAsync();

        await EditAsync(tracker, "edit_file", file, () => Task.CompletedTask);

        (await tracker.SnapshotAsync(_run, TestContext.Current.CancellationToken)).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public async Task ShouldIgnoreFilesChangedOutsideBuiltInFileTools()
    {
        var file = Path("command-output.txt");
        var tracker = await StartAsync();

        await File.WriteAllTextAsync(file, "created by a command", TestContext.Current.CancellationToken);
        await tracker.RecordEffectAsync(
            _run, BuiltIn("process_run"), "{}", TestContext.Current.CancellationToken);

        (await tracker.SnapshotAsync(_run, TestContext.Current.CancellationToken)).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public async Task ShouldIgnoreAPathNoGrantCovers()
    {
        // The path comes back from a tool, so it is re-checked here rather than trusted.
        var outside = Directory.CreateTempSubdirectory("aiclient-outside");
        try
        {
            var file = System.IO.Path.Combine(outside.FullName, "secret.txt");
            var tracker = await StartAsync();

            await EditAsync(tracker, "write_file", file, () => File.WriteAllTextAsync(file, "written"));

            (await tracker.SnapshotAsync(_run, TestContext.Current.CancellationToken)).IsEmpty.ShouldBeTrue();
        }
        finally { outside.Delete(true); }
    }

    [Fact]
    public async Task ShouldForgetARunOnceItCompletes()
    {
        var file = Path("a.txt");
        var tracker = await StartAsync();
        await EditAsync(tracker, "write_file", file, () => File.WriteAllTextAsync(file, "a"));

        await tracker.CompleteRunAsync(_run, TestContext.Current.CancellationToken);

        (await tracker.SnapshotAsync(_run, TestContext.Current.CancellationToken)).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public async Task ShouldNotCountLinesForABinaryFile()
    {
        var file = Path("blob.bin");
        await File.WriteAllBytesAsync(file, [1, 0, 2, 0, 3], TestContext.Current.CancellationToken);
        var tracker = await StartAsync();

        await EditAsync(tracker, "write_file", file, () => File.WriteAllBytesAsync(file, [1, 0, 2, 0, 3, 4]));

        var change = (await tracker.SnapshotAsync(_run, TestContext.Current.CancellationToken)).Files.ShouldHaveSingleItem();
        change.IsBinary.ShouldBeTrue();
        change.Additions.ShouldBeNull();
        change.Confidence.ShouldBe(FileChangeConfidence.Approximate);
    }

    [Fact]
    public async Task ShouldCoverASubtaskStillRunningInTheCallersSnapshot()
    {
        var file = Path("delegated.txt");
        var tracker = await StartAsync();
        var child = Child();
        await BeginAsync(tracker, child, _run);

        await EditAsync(tracker, child, "write_file", file, () => File.WriteAllTextAsync(file, "a\nb"));

        // No CompleteRunAsync: the caller is shown the total while the subtask is still working.
        var change = (await tracker.SnapshotAsync(_run, TestContext.Current.CancellationToken)).Files.ShouldHaveSingleItem();
        change.Path.ShouldBe(file);
        change.Additions.ShouldBe(2);
    }

    [Fact]
    public async Task ShouldKeepASubtasksChangesAfterItHasFinished()
    {
        var file = Path("delegated.txt");
        var tracker = await StartAsync();
        var child = Child();
        await BeginAsync(tracker, child, _run);
        await EditAsync(tracker, child, "write_file", file, () => File.WriteAllTextAsync(file, "a\nb"));

        await tracker.CompleteRunAsync(child, TestContext.Current.CancellationToken);

        var change = (await tracker.SnapshotAsync(_run, TestContext.Current.CancellationToken)).Files.ShouldHaveSingleItem();
        change.Additions.ShouldBe(2);
    }

    [Fact]
    public async Task ShouldRollUpChangesThroughNestedSubtasks()
    {
        var file = Path("deep.txt");
        var tracker = await StartAsync();
        var child = Child();
        var grandchild = Child();
        await BeginAsync(tracker, child, _run);
        await BeginAsync(tracker, grandchild, child);

        await EditAsync(tracker, grandchild, "write_file", file, () => File.WriteAllTextAsync(file, "a"));

        (await tracker.SnapshotAsync(_run, TestContext.Current.CancellationToken)).Files.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ShouldMeasureAFileTouchedByBothTheCallerAndASubtaskFromTheCallersBaseline()
    {
        var file = Path("shared.txt");
        await File.WriteAllTextAsync(file, "one", TestContext.Current.CancellationToken);
        var tracker = await StartAsync();
        await EditAsync(tracker, "edit_file", file, () => File.WriteAllTextAsync(file, "one\ntwo"));
        var child = Child();
        await BeginAsync(tracker, child, _run);

        await EditAsync(tracker, child, "edit_file", file, () => File.WriteAllTextAsync(file, "one\ntwo\nthree"));

        // One row, and both added lines counted: measuring from the subtask's own baseline would
        // report a single addition and lose the caller's.
        var change = (await tracker.SnapshotAsync(_run, TestContext.Current.CancellationToken)).Files.ShouldHaveSingleItem();
        change.Additions.ShouldBe(2);
        change.Deletions.ShouldBe(0);
    }

    [Fact]
    public async Task ShouldNotCoverASubtaskOfAnotherRun()
    {
        var file = Path("elsewhere.txt");
        var tracker = await StartAsync();
        var stranger = Child();
        var child = Child();
        await BeginAsync(tracker, stranger, null);
        await BeginAsync(tracker, child, stranger);

        await EditAsync(tracker, child, "write_file", file, () => File.WriteAllTextAsync(file, "a"));

        (await tracker.SnapshotAsync(_run, TestContext.Current.CancellationToken)).IsEmpty.ShouldBeTrue();
    }
}
