namespace AI.Server.Tests.Hosting;

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AI.Contracts.Chats;
using AI.Contracts.Navigation;
using AI.Contracts.Runs;
using AI.Contracts.Workspace;
using AI.Application.Notifications;
using AI.Application.Runs;
using AI.Server.Hosting;
using Microsoft.AspNetCore.Http;
using Moq;
using Shouldly;
using Xunit;

public sealed class RunEventsPublisherTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ChatId = Guid.NewGuid();

    [Fact]
    public async Task ShouldSendDraftGrowthAsAppend()
    {
        var run = CreateRun();
        var frames = await PublishAsync(run, run with { DraftContent = "Reading" }, run with { DraftContent = "Reading the file" });

        frames[1].Runs.ShouldBeEmpty();
        frames[1].DraftAppends.ShouldHaveSingleItem().ShouldBe(new ChatRunDraftAppend(ChatId, ChatId, run.Revision, 0, "Reading"));
        frames[2].DraftAppends.ShouldHaveSingleItem().ShouldBe(new ChatRunDraftAppend(ChatId, ChatId, run.Revision, 7, " the file"));
    }

    [Fact]
    public async Task ShouldLeaveOutMessagesTheStreamAlreadySent()
    {
        var first = Append(0, 1, "Question");
        var second = Append(1, 2, "Step");
        var run = CreateRun() with { ChatRevision = 1, MessageDelta = new ChatMessageDelta([first]) };
        var next = run with { ChatRevision = 2, Revision = run.Revision + 1, MessageDelta = new ChatMessageDelta([first, second]) };

        var frames = await PublishAsync(run, next);

        frames[0].Runs.ShouldHaveSingleItem().MessageDelta!.Appends.ShouldHaveSingleItem().Message.Content.ShouldBe("Question");
        frames[1].Runs.ShouldHaveSingleItem().MessageDelta!.Appends.ShouldHaveSingleItem().Message.Content.ShouldBe("Step");
    }

    [Fact]
    public async Task ShouldLeaveOutWorkspaceChangesTheStreamAlreadySent()
    {
        var changes = new WorkspaceChangeSet([new FileChange("a.txt", FileChangeKind.Modified, 1, 1, Diff: "-a\n+b")], 1, 1);
        var run = CreateRun() with { WorkspaceChanges = changes };
        var started = run with { Revision = run.Revision + 1, DraftToolCall = "write_file" };
        var changed = started with
        {
            Revision = started.Revision + 1,
            WorkspaceChanges = new WorkspaceChangeSet([.. changes.Files, new FileChange("b.txt", FileChangeKind.Added, 2, 0)], 3, 1)
        };

        var frames = await PublishAsync(run, started, changed);

        frames[0].Runs.ShouldHaveSingleItem().WorkspaceChanges.ShouldNotBeNull();
        frames[1].Runs.ShouldHaveSingleItem().WorkspaceChanges.ShouldBeNull();
        frames[1].KeptWorkspaceChanges.ShouldHaveSingleItem().ShouldBe(new ChatRunKey(ChatId, ChatId));
        frames[2].Runs.ShouldHaveSingleItem().WorkspaceChanges!.Files.Count.ShouldBe(2);
        frames[2].KeptWorkspaceChanges.ShouldBeEmpty();
    }

    private static ChatRunSnapshot CreateRun() =>
        new(ProjectId, ChatId, ChatId, ChatRunStatus.Generating, string.Empty, [], false, null, 3);

    private static ChatMessageAppend Append(long baseRevision, long revision, string content) =>
        new(baseRevision, revision, new ChatMessageView(Guid.NewGuid(), null, "Assistant", content, DateTimeOffset.UtcNow));

    private static async Task<IReadOnlyList<ChatRunSnapshotUpdate>> PublishAsync(params ChatRunSnapshot[] runs)
    {
        var dispatcher = new Mock<IChatRunDispatcher>();
        dispatcher.Setup(item => item.SubscribeAsync(It.IsAny<CancellationToken>())).Returns(Sequence(runs.Select(run => (IReadOnlyList<ChatRunSnapshot>)[run])));
        var changes = new Mock<IAppDataChangeSignal>();
        changes.Setup(item => item.SubscribeAsync(It.IsAny<CancellationToken>())).Returns(Sequence<long>([]));
        var navigation = new Mock<IAppNavigationSignal>();
        navigation.Setup(item => item.SubscribeAsync(It.IsAny<CancellationToken>())).Returns(Sequence<AppNavigation>([]));
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;

        await new RunEventsPublisher(dispatcher.Object, changes.Object, navigation.Object, new RunSnapshotComparer())
            .WriteAsync(context.Response, CancellationToken.None);

        return Encoding.UTF8.GetString(body.ToArray())
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Where(frame => frame.StartsWith("event: snapshot\n", StringComparison.Ordinal))
            .Select(frame => JsonSerializer.Deserialize<ChatRunSnapshotUpdate>(frame[(frame.IndexOf("data: ", StringComparison.Ordinal) + 6)..])!)
            .ToArray();
    }

    private static async IAsyncEnumerable<T> Sequence<T>(IEnumerable<T> items, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
