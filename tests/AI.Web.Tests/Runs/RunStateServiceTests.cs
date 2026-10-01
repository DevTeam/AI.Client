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

    [Fact]
    public void ShouldExtendDraftFromTheLengthItWasMadeFrom()
    {
        var chatId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var service = new RunStateService();
        service.Store(new ChatRunSnapshot(Guid.NewGuid(), chatId, branchId, ChatRunStatus.Generating,
            string.Empty, [], false, null, 5, ChatRevision: 7, DraftContent: "Reading"));

        service.AppendDraft([new ChatRunDraftAppend(chatId, branchId, 5, 7, " the file")]);

        var updated = service.Runs[new RunKey(chatId, branchId)];
        updated.DraftContent.ShouldBe("Reading the file");
        updated.Revision.ShouldBe(5);
        updated.ChatRevision.ShouldBe(7);
    }

    [Fact]
    public void ShouldStartDraftWhenRunHasNone()
    {
        var chatId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var service = new RunStateService();
        service.Store(new ChatRunSnapshot(Guid.NewGuid(), chatId, branchId, ChatRunStatus.Generating,
            string.Empty, [], false, null, 5));

        service.AppendDraft([new ChatRunDraftAppend(chatId, branchId, 5, 0, "Reading")]);

        service.Runs[new RunKey(chatId, branchId)].DraftContent.ShouldBe("Reading");
    }

    [Theory]
    [InlineData(5, 3)]
    [InlineData(4, 7)]
    [InlineData(6, 7)]
    public void ShouldIgnoreDraftAppendMadeFromAnotherDraft(long revision, int baseLength)
    {
        var chatId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var service = new RunStateService();
        service.Store(new ChatRunSnapshot(Guid.NewGuid(), chatId, branchId, ChatRunStatus.Generating,
            string.Empty, [], false, null, 5, DraftContent: "Reading"));

        service.AppendDraft([new ChatRunDraftAppend(chatId, branchId, revision, baseLength, " the file")]);

        service.Runs[new RunKey(chatId, branchId)].DraftContent.ShouldBe("Reading");
    }
}
