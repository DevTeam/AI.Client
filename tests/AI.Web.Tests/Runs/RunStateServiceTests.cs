namespace AI.Web.Tests.State;

using AI.Contracts.Runs;
using AI.Web.Runs;
using Shouldly;
using Xunit;

public sealed class RunStateServiceTests
{
    [Fact]
    public void ShouldApplyStreamingAppendWithoutReplacingOtherSnapshotState()
    {
        var projectId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var service = new RunStateService();
        service.Store(new ChatRunSnapshot(projectId, chatId, branchId, ChatRunStatus.Generating,
            "Hello", [], false, null, 4, ChatRevision: 7, BranchRevision: 3));

        service.AppendStreaming([new ChatRunStreamingAppend(chatId, branchId, 5, " world")]);

        var updated = service.Runs[new RunKey(chatId, branchId)];
        updated.StreamingContent.ShouldBe("Hello world");
        updated.Revision.ShouldBe(5);
        updated.ChatRevision.ShouldBe(7);
        updated.BranchRevision.ShouldBe(3);
    }

    [Fact]
    public void ShouldIgnoreStaleStreamingAppend()
    {
        var chatId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var service = new RunStateService();
        service.Store(new ChatRunSnapshot(Guid.NewGuid(), chatId, branchId, ChatRunStatus.Generating,
            "Current", [], false, null, 5));

        service.AppendStreaming([new ChatRunStreamingAppend(chatId, branchId, 4, " stale")]);

        service.Runs[new RunKey(chatId, branchId)].StreamingContent.ShouldBe("Current");
    }

    [Fact]
    public void ShouldIgnoreRepeatedStreamingAppendRevision()
    {
        var chatId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var service = new RunStateService();
        service.Store(new ChatRunSnapshot(Guid.NewGuid(), chatId, branchId, ChatRunStatus.Generating,
            "Current", [], false, null, 5));

        service.AppendStreaming([new ChatRunStreamingAppend(chatId, branchId, 5, " duplicate")]);

        service.Runs[new RunKey(chatId, branchId)].StreamingContent.ShouldBe("Current");
    }
}
