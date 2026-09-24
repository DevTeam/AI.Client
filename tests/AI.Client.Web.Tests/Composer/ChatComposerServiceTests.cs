namespace AI.Client.Web.Tests.Composer;

using AI.Client.Contracts.Chats;
using AI.Client.Contracts.Runs;
using AI.Client.Contracts.Resources;
using Chats;
using AI.Client.Web.Composer;
using Runs;
using Moq;
using Shouldly;
using Xunit;

public class ChatComposerServiceTests
{
    [Fact]
    public async Task ShouldSubmitReviewOnlyMessageToExistingChat()
    {
        var history = new Mock<IChatHistoryApi>(MockBehavior.Strict);
        var runs = new Mock<IChatRunsApi>(MockBehavior.Strict);
        var chatId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var review = new ChatResourceRef(Guid.NewGuid(), ChatResourceKind.Review, string.Empty,
            "Message review", ChatReviewKind.Message);
        var chat = new ChatDetails(chatId, projectId, "Chat", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            1, null, [], [new ChatBranchView(chatId, null, "Main")]);
        runs.Setup(api => api.SubmitAsync(projectId, chatId,
                It.Is<SubmitChatMessageRequest>(request => request.Content == string.Empty
                    && request.Resources != null && request.Resources.Count == 1
                    && request.Resources[0].Id == review.Id), CancellationToken.None))
            .ReturnsAsync(new ChatRunSnapshot(projectId, chatId, chatId, ChatRunStatus.Idle, "", [], false, null, 1));

        var result = await new ChatComposerService(history.Object, runs.Object).SubmitAsync(
            new ComposerSubmitRequest(ComposerSubmitMode.Send, projectId, chat, null, null, null,
                null, null, null, string.Empty, null, Resources: [review]), CancellationToken.None);

        result.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        runs.VerifyAll();
        history.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(ComposerSubmitMode.Send, ChatSubmitMode.Send)]
    [InlineData(ComposerSubmitMode.Queue, ChatSubmitMode.Queue)]
    [InlineData(ComposerSubmitMode.Fork, ChatSubmitMode.Fork)]
    [InlineData(ComposerSubmitMode.SendNow, ChatSubmitMode.SendNow)]
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
        var result = await service.SubmitAsync(new ComposerSubmitRequest(mode, projectId, chat, null, null, null, null, null, null, "hello", null), CancellationToken.None);
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
        var chat = new ChatDetails(chatId, projectId, "Chat", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 7, null, [],
            [new ChatBranchView(branchId, sourceId, "Branch", chatId, sourceId, 3)]);
        runs.Setup(api => api.SubmitAsync(projectId, chatId,
            It.Is<SubmitChatMessageRequest>(request => request.Mode == ChatSubmitMode.Replace
                && request.ExpectedBranchRevision == 3 && request.ReplaceSourceId == sourceId && request.BranchId == branchId),
            CancellationToken.None)).ThrowsAsync(new HttpRequestException("Unavailable"));
        var result = await new ChatComposerService(history.Object, runs.Object).SubmitAsync(
            new ComposerSubmitRequest(ComposerSubmitMode.Queue, projectId, chat, null, null, sourceId, null, null, null, "replacement", null, branchId), CancellationToken.None);
        result.ShouldBeOfType<ComposerSubmitOutcome.Rejected>();
        history.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EditAndBranchShouldNotInheritQueueMode()
    {
        var history = new Mock<IChatHistoryApi>(MockBehavior.Strict);
        var runs = new Mock<IChatRunsApi>(MockBehavior.Strict);
        var chatId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var chat = new ChatDetails(chatId, projectId, "Chat", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, null, [],
            [new ChatBranchView(chatId, sourceId, "Main")]);
        runs.Setup(api => api.SubmitAsync(projectId, chatId,
                It.Is<SubmitChatMessageRequest>(request => request.Mode == ChatSubmitMode.Fork
                    && request.ParentMessageId == sourceId), CancellationToken.None))
            .ReturnsAsync(new ChatRunSnapshot(projectId, chatId, Guid.NewGuid(), ChatRunStatus.Idle, "", [], false, null, 1));

        var result = await new ChatComposerService(history.Object, runs.Object).SubmitAsync(
            new ComposerSubmitRequest(ComposerSubmitMode.Queue, projectId, chat, sourceId, sourceId, null,
                null, null, null, "branch", null, chatId), CancellationToken.None);

        result.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        runs.VerifyAll();
        history.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ForkingAnEditedRootShouldUseRootParentMode()
    {
        var history = new Mock<IChatHistoryApi>(MockBehavior.Strict);
        var runs = new Mock<IChatRunsApi>(MockBehavior.Strict);
        var chatId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var chat = new ChatDetails(chatId, projectId, "Chat", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, null, [],
            [new ChatBranchView(chatId, Guid.NewGuid(), "Main")]);
        runs.Setup(api => api.SubmitAsync(projectId, chatId,
                It.Is<SubmitChatMessageRequest>(request => request.Mode == ChatSubmitMode.Fork
                    && request.ParentMode == MessageParentMode.Root && request.ParentMessageId == null), CancellationToken.None))
            .ReturnsAsync(new ChatRunSnapshot(projectId, chatId, Guid.NewGuid(), ChatRunStatus.Idle, "", [], false, null, 1));

        var result = await new ChatComposerService(history.Object, runs.Object).SubmitAsync(
            new ComposerSubmitRequest(ComposerSubmitMode.Fork, projectId, chat, null, null, null,
                null, null, null, "alternative root", null, chatId), CancellationToken.None);

        result.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        runs.VerifyAll();
        history.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReplacementShouldResolveAnUnrecoverableFailedRun()
    {
        var history = new Mock<IChatHistoryApi>(MockBehavior.Strict);
        var runs = new Mock<IChatRunsApi>(MockBehavior.Strict);
        var chatId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var chat = new ChatDetails(chatId, projectId, "Chat", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 2, null, [],
            [new ChatBranchView(chatId, sourceId, "Main", Revision: 2)]);
        var failed = new ChatRunSnapshot(projectId, chatId, chatId, ChatRunStatus.Failed, "", [], false, "Broken", 3,
            FailureCode: RunFailureCode.ParentMissing, CanRetry: false, BranchRevision: 2);
        runs.Setup(api => api.SubmitAsync(projectId, chatId,
                It.Is<SubmitChatMessageRequest>(request => request.Mode == ChatSubmitMode.Replace), CancellationToken.None))
            .ReturnsAsync(failed with { Status = ChatRunStatus.Idle, Error = null });

        var result = await new ChatComposerService(history.Object, runs.Object).SubmitAsync(
            new ComposerSubmitRequest(ComposerSubmitMode.Send, projectId, chat, null, null, sourceId,
                null, null, null, "replacement", failed, chatId), CancellationToken.None);

        result.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        runs.VerifyAll();
        history.VerifyNoOtherCalls();
    }
}
