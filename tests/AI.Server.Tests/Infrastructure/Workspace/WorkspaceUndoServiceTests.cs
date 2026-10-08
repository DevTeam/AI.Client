namespace AI.Infrastructure.Tests.Workspace;

using System.Text.Json;
using AI.Application.Projects;
using AI.Application.Tools;
using AI.Application.Workspace;
using AI.Contracts.Projects;
using AI.Contracts.Tools;
using AI.Contracts.Workspace;
using AI.Infrastructure.Storage;
using AI.Infrastructure.Workspace;
using AI.Application.Resources;
using Moq;
using Shouldly;
using Xunit;

[Trait("Category", "Integration")]
public sealed class WorkspaceUndoServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aiclient-undo").FullName;
    private readonly Guid _projectId = Guid.NewGuid();
    private readonly Guid _chatId = Guid.NewGuid();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private (WorkspaceUndoService Undo, WorkspaceChangeTracker Tracker) Create()
    {
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(item => item.RootDirectory).Returns(Path.Combine(_root, "data"));
        var projects = new Mock<IProjectService>();
        projects.Setup(item => item.GetAsync(_projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectDetails(_projectId, "Test", string.Empty,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1,
                [new DirectoryGrantSettings(Guid.NewGuid(), "Workspace", _root, true, ["read", "write"])], [], []));
        var assets = new ResourceAssetService(location.Object, projects.Object);
        var undo = new WorkspaceUndoService(location.Object, new PhysicalTextFileSystem(), assets,
            projects.Object, new ProjectPathAccess());
        return (undo, new WorkspaceChangeTracker(new LineDiff(), undo));
    }

    private static async Task EditAsync(WorkspaceChangeTracker tracker, WorkspaceRunKey run, string name,
        string path, Func<Task> change)
    {
        var descriptor = ToolDescriptor.Basic(ToolRef.BuiltInPrefix + name, name, name,
            JsonDocument.Parse("{}").RootElement.Clone());
        var arguments = JsonSerializer.Serialize(new { path });
        await tracker.RecordIntentAsync(run, descriptor, arguments, TestContext.Current.CancellationToken);
        await change();
        await tracker.RecordEffectAsync(run, descriptor, arguments, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UndoSurvivesRestartAndRestoresTheUserBaseline()
    {
        var path = Path.Combine(_root, "note.txt");
        await File.WriteAllTextAsync(path, "user draft", TestContext.Current.CancellationToken);
        var (undo, tracker) = Create();
        using (undo)
        {
            var run = new WorkspaceRunKey(_projectId, _chatId, Guid.NewGuid());
            await tracker.BeginRunAsync(run, [new ToolDirectoryGrant(_root, true, ["edit"])], null,
                TestContext.Current.CancellationToken);
            await EditAsync(tracker, run, "edit_file", path,
                () => File.WriteAllTextAsync(path, "user draft\nagent line", TestContext.Current.CancellationToken));
            var changes = await tracker.SnapshotAsync(run, TestContext.Current.CancellationToken);
            var id = (await tracker.CaptureUndoAsync(run, changes, TestContext.Current.CancellationToken)).ShouldNotBeNull();
            await tracker.CompleteRunAsync(run, TestContext.Current.CancellationToken);

            var (reopened, _) = Create();
            using (reopened)
            {
                (await reopened.StatusAsync(_projectId, _chatId, id, TestContext.Current.CancellationToken))!
                    .Files.Single().State.ShouldBe(WorkspaceUndoFileState.Ready);
                var result = await reopened.UndoAsync(_projectId, _chatId, id, null,
                    TestContext.Current.CancellationToken);
                result!.AppliedCount.ShouldBe(1);
                result.Files.Single().State.ShouldBe(WorkspaceUndoFileState.Undone);
                (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ShouldBe("user draft");
            }
        }
    }

    [Fact]
    public async Task BulkUndoStopsAtLaterEditWhileIndividualFileCanBeUndone()
    {
        var first = Path.Combine(_root, "first.txt");
        var second = Path.Combine(_root, "second.txt");
        await File.WriteAllTextAsync(first, "first original", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(second, "second original", TestContext.Current.CancellationToken);
        var (undo, tracker) = Create();
        using (undo)
        {
            var run = new WorkspaceRunKey(_projectId, _chatId, Guid.NewGuid());
            await tracker.BeginRunAsync(run, [new ToolDirectoryGrant(_root, true, ["edit"])], null,
                TestContext.Current.CancellationToken);
            await EditAsync(tracker, run, "edit_file", first, () => File.WriteAllTextAsync(first, "agent first", TestContext.Current.CancellationToken));
            await EditAsync(tracker, run, "edit_file", second, () => File.WriteAllTextAsync(second, "agent second", TestContext.Current.CancellationToken));
            var changes = await tracker.SnapshotAsync(run, TestContext.Current.CancellationToken);
            var id = (await tracker.CaptureUndoAsync(run, changes, TestContext.Current.CancellationToken)).ShouldNotBeNull();
            await tracker.CompleteRunAsync(run, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(second, "user later edit", TestContext.Current.CancellationToken);

            var bulk = await undo.UndoAsync(_projectId, _chatId, id, null, TestContext.Current.CancellationToken);
            bulk!.AppliedCount.ShouldBe(0);
            (await File.ReadAllTextAsync(first, TestContext.Current.CancellationToken)).ShouldBe("agent first");
            (await File.ReadAllTextAsync(second, TestContext.Current.CancellationToken)).ShouldBe("user later edit");
            var single = await undo.UndoAsync(_projectId, _chatId, id, first, TestContext.Current.CancellationToken);
            single!.AppliedCount.ShouldBe(1);
            (await File.ReadAllTextAsync(first, TestContext.Current.CancellationToken)).ShouldBe("first original");
            (await File.ReadAllTextAsync(second, TestContext.Current.CancellationToken)).ShouldBe("user later edit");
        }
    }

    [Fact]
    public async Task UndoRestoresDeletedBinaryBytesAndRemovesCreatedEmptyFile()
    {
        var deleted = Path.Combine(_root, "binary.bin");
        var created = Path.Combine(_root, "empty.txt");
        var bytes = new byte[] { 0, 1, 2, 255 };
        await File.WriteAllBytesAsync(deleted, bytes, TestContext.Current.CancellationToken);
        var (undo, tracker) = Create();
        using (undo)
        {
            var run = new WorkspaceRunKey(_projectId, _chatId, Guid.NewGuid());
            await tracker.BeginRunAsync(run, [new ToolDirectoryGrant(_root, true, ["edit"])], null,
                TestContext.Current.CancellationToken);
            await EditAsync(tracker, run, "delete_file", deleted, () => { File.Delete(deleted); return Task.CompletedTask; });
            await EditAsync(tracker, run, "write_file", created, () => File.WriteAllBytesAsync(created, [], TestContext.Current.CancellationToken));
            var changes = await tracker.SnapshotAsync(run, TestContext.Current.CancellationToken);
            var id = (await tracker.CaptureUndoAsync(run, changes, TestContext.Current.CancellationToken)).ShouldNotBeNull();
            var result = await undo.UndoAsync(_projectId, _chatId, id, null, TestContext.Current.CancellationToken);
            result!.AppliedCount.ShouldBe(2);
            (await File.ReadAllBytesAsync(deleted, TestContext.Current.CancellationToken)).ShouldBe(bytes);
            File.Exists(created).ShouldBeFalse();
        }
    }
}
