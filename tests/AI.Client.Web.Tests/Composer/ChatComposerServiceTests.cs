namespace AI.Client.Web.Tests.Composer;

using AI.Client.Contracts.Chats;
using AI.Client.Contracts.Runs;
using Chats;
using AI.Client.Web.Composer;
using Runs;
using Moq;
using Shouldly;
using Xunit;

public class ChatComposerServiceTests
{
    [Theory]
    [InlineData(ComposerSubmitMode.Send, ChatSubmitMode.Send)]
    [InlineData(ComposerSubmitMode.Queue, ChatSubmitMode.Queue)]
    [InlineData(ComposerSubmitMode.Fork, ChatSubmitMode.Fork)]
    public async Task ShouldSubmitOneServerCommand(ComposerSubmitMode mode, ChatSubmitMode expected)
    {
        var history = new Mock<IChatHistoryApi>(MockBehavior.Strict);
        var runs = new Mock<IChatRunsApi>(MockBehavior.Strict);
        var chatId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var chat = new ChatDetails(chatId, projectId, "Chat", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, null, [],
            [new ChatBranchView(chatId, null, "Main")]);
        runs.Setup(api => api.SubmitAsync(projectId, chatId, It.Is<SubmitChatMessageRequest>(request => request.Mode == expected), CancellationToken.None))
            .ReturnsAsync(new ChatRunSnapshot(projectId, chatId, chatId, ChatRunStatus.Idle, "", [], false, null, 1));
        var service = new ChatComposerService(history.Object, runs.Object);
        var result = await service.SubmitAsync(new ComposerSubmitRequest(mode, projectId, chat, null, null, null, false, null, null, null, "hello", null), CancellationToken.None);
        result.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        runs.VerifyAll();
        history.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ShouldNotDeleteBranchBeforeSubmittingReplacement()
    {
        var history = new Mock<IChatHistoryApi>(MockBehavior.Strict);
        var runs = new Mock<IChatRunsApi>(MockBehavior.Strict);
        var chatId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var chat = new ChatDetails(chatId, projectId, "Chat", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 7, null, []);
        runs.Setup(api => api.SubmitAsync(projectId, chatId,
            It.Is<SubmitChatMessageRequest>(request => request.Mode == ChatSubmitMode.Replace && request.ExpectedRevision == 7 && request.ReplaceSourceId == sourceId && request.BranchId == branchId),
            CancellationToken.None)).ThrowsAsync(new HttpRequestException("Unavailable"));
        var result = await new ChatComposerService(history.Object, runs.Object).SubmitAsync(
            new ComposerSubmitRequest(ComposerSubmitMode.Send, projectId, chat, null, null, sourceId, false, null, null, null, "replacement", null, branchId), CancellationToken.None);
        result.ShouldBeOfType<ComposerSubmitOutcome.Rejected>();
        history.VerifyNoOtherCalls();
    }
}
