namespace AI.Server.Tests.Hosting;

using AI.Contracts.Resources;
using AI.Contracts.Runs;
using AI.Server.Hosting;
using Shouldly;
using Xunit;

public class RunSnapshotComparerTests
{
    [Fact]
    public void ShouldRecognizeStreamingAppendWhenQueueResourcesAreRemapped()
    {
        var resource = new ChatResourceRef(Guid.NewGuid(), ChatResourceKind.File, "C:\\work\\file.cs");
        var queued = new QueuedChatMessage(Guid.NewGuid(), string.Empty, DateTimeOffset.UtcNow,
            Resources: [resource]);
        var projectId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var old = new ChatRunSnapshot(projectId, chatId, branchId, ChatRunStatus.Generating,
            "A", [queued], false, null, 1);
        var current = old with
        {
            StreamingContent = "AB",
            Revision = 2,
            Queue = [queued with { Resources = [resource with { }] }]
        };

        new RunSnapshotComparer().IsStreamingAppend(old, current).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Reading")]
    public void ShouldRecognizeDraftAppend(string? previousDraft)
    {
        var old = CreateSnapshot() with { DraftContent = previousDraft };
        var current = old with { DraftContent = "Reading the file" };

        var comparer = new RunSnapshotComparer();

        comparer.IsDraftAppend(old, current).ShouldBeTrue();
        comparer.IsStreamingAppend(old, current).ShouldBeFalse();
    }

    [Fact]
    public void ShouldNotTreatReplacedDraftAsAppend()
    {
        var old = CreateSnapshot() with { DraftContent = "Reading the file" };

        new RunSnapshotComparer().IsDraftAppend(old, old with { DraftContent = "Writing" }).ShouldBeFalse();
    }

    [Fact]
    public void ShouldNotTreatDraftGrowthWithOtherChangesAsAppend()
    {
        var old = CreateSnapshot() with { DraftContent = "Reading" };
        var comparer = new RunSnapshotComparer();

        comparer.IsDraftAppend(old, old with { DraftContent = "Reading the file", DraftToolCall = "read_file" }).ShouldBeFalse();
        comparer.IsDraftAppend(old, old with { DraftContent = "Reading the file", Revision = old.Revision + 1 }).ShouldBeFalse();
        comparer.IsDraftAppend(old, old with { DraftContent = "Reading the file", Status = ChatRunStatus.Paused }).ShouldBeFalse();
    }

    [Fact]
    public void ShouldNotTreatStreamingGrowthWithDraftChangeAsStreamingAppend()
    {
        var old = CreateSnapshot() with { StreamingContent = "A", DraftContent = "Reading" };

        new RunSnapshotComparer().IsStreamingAppend(old, old with { StreamingContent = "AB", DraftContent = null }).ShouldBeFalse();
    }

    private static ChatRunSnapshot CreateSnapshot() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        ChatRunStatus.Generating, string.Empty, [], false, null, 1);
}
