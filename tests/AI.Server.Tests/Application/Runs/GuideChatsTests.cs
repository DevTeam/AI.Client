namespace AI.Application.Tests.Runs;

using AI.Application.Chats;
using AI.Application.Notifications;
using AI.Application.Runs;
using AI.Application.Settings;
using AI.Contracts.Chats;
using AI.Contracts.Runs;
using AI.Contracts.Settings;
using AI.Domain.Chats;
using AI.Domain.Projects;
using Moq;
using Shouldly;
using Xunit;

public sealed class GuideChatsTests
{
    private static readonly Guid Disabled = Guid.NewGuid();
    private static readonly Guid Default = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static readonly GlobalSettings Settings = new(
    [
        new ConnectionSettings(Disabled, "Off", "http://off", "off", false, false, true),
        new ConnectionSettings(Other, "Other", "http://other", "other", true, false, true),
        new ConnectionSettings(Default, "Default", "http://default", "default", true, true, true)
    ], [], []);

    [Fact]
    public void ShouldSkipADisabledProjectConnectionAndFallBackToTheDefault()
    {
        var chats = new GuideChats(Mock.Of<IChatRepository>(), Mock.Of<IChatRunDispatcher>(), Mock.Of<IChatService>(), Mock.Of<IAppDataChangeSignal>(), new ConnectionChoice());

        chats.PickConnection(Settings, Disabled, Disabled).ShouldBe(Default);
        chats.PickConnection(Settings, null, Other).ShouldBe(Other);
        chats.PickConnection(Settings, Other, Default).ShouldBe(Other);
    }

    [Fact]
    public void ShouldPickNoConnectionWhenNoneIsEnabled()
    {
        var chats = new GuideChats(Mock.Of<IChatRepository>(), Mock.Of<IChatRunDispatcher>(), Mock.Of<IChatService>(), Mock.Of<IAppDataChangeSignal>(), new ConnectionChoice());

        chats.PickConnection(Settings with { Connections = [Settings.Connections[0]] }, Disabled, null).ShouldBeNull();
    }

    [Fact]
    public async Task ShouldDeleteOnlyGuideChatsWithoutARunInProgress()
    {
        var projectId = Guid.NewGuid();
        var finished = Guid.NewGuid();
        var running = Guid.NewGuid();
        var ordinary = Guid.NewGuid();
        var repository = new Mock<IChatRepository>();
        repository.Setup(item => item.ListSummariesAsync(new ProjectId(projectId), It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Summary(finished, projectId, true), Summary(running, projectId, true), Summary(ordinary, projectId, false)
        ]);
        var runs = new Mock<IChatRunDispatcher>(MockBehavior.Strict);
        runs.Setup(item => item.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new ChatRunSnapshot(projectId, running, running, ChatRunStatus.Generating, "", [], false, null, 1, IsGuide: true),
            new ChatRunSnapshot(projectId, finished, finished, ChatRunStatus.Failed, "", [], false, "No model", 1, IsGuide: true)
        ]);
        runs.Setup(item => item.DeleteChatAsync(projectId, finished, 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatDeleteResult(true, 7));

        var deleted = await new GuideChats(repository.Object, runs.Object, Mock.Of<IChatService>(), Mock.Of<IAppDataChangeSignal>(), new ConnectionChoice()).CleanUpAsync(projectId, null, CancellationToken.None);

        deleted.ShouldBe(1);
        runs.Verify(item => item.DeleteChatAsync(projectId, finished, 7, It.IsAny<CancellationToken>()), Times.Once);
        runs.Verify(item => item.DeleteChatAsync(It.IsAny<Guid>(), running, It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        runs.Verify(item => item.DeleteChatAsync(It.IsAny<Guid>(), ordinary, It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ShouldDeleteAnUntouchedDemoChatButKeepOneThePersonWroteIn()
    {
        var projectId = Guid.NewGuid();
        var untouched = Guid.NewGuid();
        var used = Guid.NewGuid();
        var namesake = Guid.NewGuid();
        var repository = new Mock<IChatRepository>();
        repository.Setup(item => item.ListSummariesAsync(new ProjectId(projectId), It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Summary(untouched, projectId, false, GuideChats.DemoTitle), Summary(used, projectId, false, GuideChats.DemoTitle),
            Summary(namesake, projectId, false, GuideChats.DemoTitle)
        ]);
        var chats = new Mock<IChatService>();
        chats.Setup(item => item.GetAsync(projectId, untouched, It.IsAny<CancellationToken>())).ReturnsAsync(Details(untouched, projectId, GuideChats.DemoMode, 2));
        chats.Setup(item => item.GetAsync(projectId, used, It.IsAny<CancellationToken>())).ReturnsAsync(Details(used, projectId, GuideChats.DemoMode, 4));
        // The person's own chat with the same title is not a demo.
        chats.Setup(item => item.GetAsync(projectId, namesake, It.IsAny<CancellationToken>())).ReturnsAsync(Details(namesake, projectId, "show", 2));
        var runs = new Mock<IChatRunDispatcher>(MockBehavior.Strict);
        runs.Setup(item => item.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        runs.Setup(item => item.DeleteChatAsync(projectId, untouched, 7, It.IsAny<CancellationToken>())).ReturnsAsync(new ChatDeleteResult(true, 7));

        var deleted = await new GuideChats(repository.Object, runs.Object, chats.Object, Mock.Of<IAppDataChangeSignal>(), new ConnectionChoice())
            .CleanUpAsync(projectId, null, CancellationToken.None);

        deleted.ShouldBe(1);
        runs.Verify(item => item.DeleteChatAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static StoredChatSummary Summary(Guid id, Guid projectId, bool isGuide, string title = "Guide") =>
        new(new ChatId(id), new ProjectId(projectId), title, DateTimeOffset.UnixEpoch, 7, DateTimeOffset.UnixEpoch,
            false, null, IsGuide: isGuide);

    private static ChatDetails Details(Guid id, Guid projectId, string guideMode, int messages) =>
        new(id, projectId, GuideChats.DemoTitle, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 7, null,
            Enumerable.Range(0, messages).Select(_ => new ChatMessageView(Guid.NewGuid(), null, "User", "text", DateTimeOffset.UnixEpoch)).ToArray(),
            GuideMode: guideMode);
}
