namespace AI.Application.Tests.Workspace;

using AI.Application.Runs;
using AI.Application.Workspace;
using AI.Contracts.Runs;
using Moq;
using Shouldly;
using Xunit;

public class WorkspaceUndoGuardTests
{
    private static ChatRunSnapshot Run(Guid projectId, ChatRunStatus status) =>
        new(projectId, Guid.NewGuid(), Guid.NewGuid(), status, string.Empty, [], false, null, 1);

    private static async Task<bool> IsProjectWriting(Guid projectId, params ChatRunSnapshot[] runs)
    {
        var dispatcher = new Mock<IChatRunDispatcher>(MockBehavior.Strict);
        dispatcher.Setup(item => item.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ChatRunSnapshot>)runs);
        var guard = new WorkspaceUndoGuard(dispatcher.Object);
        return await guard.IsProjectWritingAsync(projectId, CancellationToken.None);
    }

    [Theory]
    [InlineData(ChatRunStatus.Generating, true)]
    [InlineData(ChatRunStatus.Paused, false)]
    [InlineData(ChatRunStatus.Interrupted, false)]
    [InlineData(ChatRunStatus.Completed, false)]
    [InlineData(ChatRunStatus.Idle, false)]
    public async Task ShouldBlockUndoOnlyWhileAProjectRunIsWritingFiles(ChatRunStatus status, bool expected)
    {
        var projectId = Guid.NewGuid();
        (await IsProjectWriting(projectId, Run(projectId, status))).ShouldBe(expected);
    }

    [Fact]
    public async Task ShouldIgnoreRunsOfAnotherProject()
    {
        var projectId = Guid.NewGuid();
        var other = Guid.NewGuid();
        (await IsProjectWriting(projectId, Run(other, ChatRunStatus.Generating))).ShouldBeFalse();
    }

    [Fact]
    public async Task ShouldBlockUndoWhenAnyRunOfTheProjectIsGenerating()
    {
        var projectId = Guid.NewGuid();
        (await IsProjectWriting(projectId, Run(projectId, ChatRunStatus.Paused), Run(projectId, ChatRunStatus.Generating)))
            .ShouldBeTrue();
    }
}
