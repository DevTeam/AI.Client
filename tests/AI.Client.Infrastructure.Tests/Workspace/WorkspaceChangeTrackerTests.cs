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

    private static ToolCallResult Ok() => ToolResultCodec.Read("""{"isError":false}""");

    private async Task<WorkspaceChangeTracker> StartAsync(params string[] roots)
    {
        var tracker = new WorkspaceChangeTracker();
        await tracker.BeginRunAsync(_run,
            (roots.Length == 0 ? [_root] : roots).Select(root => new ToolDirectoryGrant(root, true, ["edit"])).ToArray(),
            TestContext.Current.CancellationToken);
        return tracker;
    }

    private async Task EditAsync(WorkspaceChangeTracker tracker, string tool, string path, Func<Task> change)
    {
        var descriptor = BuiltIn(tool);
        var arguments = JsonSerializer.Serialize(new { path });
        await tracker.RecordIntentAsync(_run, descriptor, arguments, TestContext.Current.CancellationToken);
        await change();
        await tracker.RecordEffectAsync(_run, descriptor, arguments, Ok(), TestContext.Current.CancellationToken);
    }

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
        changes.IsComplete.ShouldBeTrue();
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
        await tracker.RecordEffectAsync(_run, descriptor, arguments, Ok(), TestContext.Current.CancellationToken);

        var changes = await tracker.SnapshotAsync(_run, TestContext.Current.CancellationToken);
        changes.Files.Count.ShouldBe(2);
        changes.Files.ShouldContain(file => file.Kind == FileChangeKind.Deleted);
        changes.Files.ShouldContain(file => file.Kind == FileChangeKind.Added);
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
    public async Task ShouldMarkTheSetIncompleteAfterACommandRan()
    {
        // A command can write anywhere it has permission to; claiming a complete list afterwards
        // would be a stronger statement than the Host can support.
        var tracker = await StartAsync();

        await tracker.RecordEffectAsync(_run, BuiltIn("process_run"), "{}", Ok(), TestContext.Current.CancellationToken);

        (await tracker.SnapshotAsync(_run, TestContext.Current.CancellationToken)).IsComplete.ShouldBeFalse();
    }

    [Fact]
    public async Task ShouldMarkTheSetIncompleteAfterAThirdPartyToolThatIsNotDeclaredReadOnly()
    {
        var tracker = await StartAsync();
        var thirdParty = ToolDescriptor.Basic("mcp_other__do_things", "do_things", null,
            JsonDocument.Parse("{}").RootElement.Clone());

        await tracker.RecordEffectAsync(_run, thirdParty, "{}", Ok(), TestContext.Current.CancellationToken);

        (await tracker.SnapshotAsync(_run, TestContext.Current.CancellationToken)).IsComplete.ShouldBeFalse();
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
}
