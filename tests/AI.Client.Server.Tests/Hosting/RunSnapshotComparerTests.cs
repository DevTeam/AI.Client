namespace AI.Client.Server.Tests.Hosting;

using AI.Client.Contracts.Resources;
using AI.Client.Contracts.Runs;
using AI.Client.Server.Hosting;
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
}
