namespace AI.Web.Tests;

using System.Reflection;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Runs;
using AI.Web.Chats;
using AI.Web.Composer;
using AI.Web.Notifications;
using AI.Web.Pages;
using AI.Web.Runs;
using AI.Web.State;
using Moq;
using Shouldly;
using Xunit;

public sealed class HomeBranchSelectionTests
{
    [Fact]
    public async Task NewChatShouldClearTheRememberedProjectSelection()
    {
        var page = new Home();
        var projectId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var workspace = new Mock<IWorkspaceStateService>();
        SetField(page, "_selectedProject", new ProjectSummary(projectId, "Project", string.Empty, now, 1));
        SetField(page, "_selectedChat", new ChatDetails(chatId, projectId, "Chat", now, now, 1, null, []));
        Inject(page, "WorkspaceStateService", workspace.Object);

        await (Task)Invoke(page, "CreateChatAsync")!;

        GetField(page, "_selectedChat").ShouldBeNull();
        workspace.Verify(value => value.SetProjectContextAsync(projectId, null, null, null), Times.Once);
    }

    [Fact]
    public void RememberedBranchShouldFollowItsUpdatedHead()
    {
        var page = new Home();
        var projectId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var previousHead = Guid.NewGuid();
        var currentHead = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var chat = new ChatDetails(chatId, projectId, "Chat", now, now, 2, null, [],
            [new ChatBranchView(chatId, null, "Main"), new ChatBranchView(branchId, currentHead, "Fork")]);
        SetField(page, "_selectedProject", new ProjectSummary(projectId, "Project", string.Empty, now, 1));
        Inject(page, "WorkspaceStateService", Mock.Of<IWorkspaceStateService>());
        Inject(page, "_composerHistoryNavigator", Mock.Of<IComposerHistoryNavigator>());

        Invoke(page, "ApplyRememberedChat", chat, ((Guid?)chatId, (Guid?)previousHead, (Guid?)branchId));

        GetField(page, "_selectedBranchId").ShouldBe(branchId);
        GetField(page, "_branchLeafId").ShouldBe(currentHead);
    }

    [Theory]
    [InlineData(true, ChatRunStatus.Generating)]
    [InlineData(true, ChatRunStatus.Completed)]
    [InlineData(false, ChatRunStatus.Generating)]
    [InlineData(false, ChatRunStatus.Completed)]
    public async Task SubmittedBranchUpdatesShouldFollowTheCurrentSelection(bool switchToOriginal, ChatRunStatus status)
    {
        var page = new Home();
        var runState = new RunStateService();
        var workspace = new Mock<IWorkspaceStateService>();
        workspace.Setup(value => value.GetComposerDraft(It.IsAny<string>())).Returns(string.Empty);
        Inject(page, "RunState", runState);
        Inject(page, "WorkspaceStateService", workspace.Object);
        Inject(page, "_composerHistoryNavigator", Mock.Of<IComposerHistoryNavigator>());
        Inject(page, "StatusPresentation", Mock.Of<IRunStatusPresentation>());
        Inject(page, "Notifications", Mock.Of<INotificationService>());
        Inject(page, "ChatMessageDeltaMerger", new ChatMessageDeltaMerger());

        var projectId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var parent = new ChatMessageView(Guid.NewGuid(), null, "Assistant", "Shared answer", now);
        var original = new ChatMessageView(Guid.NewGuid(), parent.Id, "User", "Original question", now.AddSeconds(1));
        var fork = new ChatMessageView(Guid.NewGuid(), parent.Id, "User", "Fork question", now.AddSeconds(2));
        var answer = new ChatMessageView(Guid.NewGuid(), fork.Id, "Assistant", "Branch answer", now.AddSeconds(3));
        var chat = new ChatDetails(chatId, projectId, "Chat", now, now, 1, null,
            [parent, original, fork],
            [new ChatBranchView(chatId, original.Id, "Main"),
                new ChatBranchView(branchId, fork.Id, "Fork", chatId, fork.Id)]);
        SetField(page, "_selectedProject", new ProjectSummary(projectId, "Project", string.Empty, now, 1));
        var submitted = new ChatRunSnapshot(projectId, chatId, branchId, ChatRunStatus.Generating,
            string.Empty, [], false, null, 1, ChatRevision: 1, HeadMessageId: fork.Id);
        Invoke(page, "ApplyAcceptedRunState", new ComposerSubmitOutcome.Accepted(chat, submitted, branchId, fork.Id, false), "draft");
        if (switchToOriginal) await (Task)Invoke(page, "SelectMessageBranch", original.Id)!;

        var update = submitted with
        {
            Status = status,
            Revision = 2,
            ChatRevision = 2,
            HeadMessageId = answer.Id,
            MessageDelta = new ChatMessageDelta([new ChatMessageAppend(1, 2, answer)])
        };
        await (Task)Invoke(page, "ApplyRunSnapshotAsync", new ChatRunSnapshotUpdate(false, [update], [], []))!;

        GetField(page, "_selectedBranchId").ShouldBe(switchToOriginal ? chatId : branchId);
        GetField(page, "_branchLeafId").ShouldBe(switchToOriginal ? original.Id : answer.Id);
        var updatedChat = (ChatDetails)GetField(page, "_selectedChat")!;
        updatedChat.Messages.ShouldContain(answer);
        updatedChat.Branches!.Single(branch => branch.Id == branchId).HeadMessageId.ShouldBe(answer.Id);
        runState.Runs[new RunKey(chatId, branchId)].Status.ShouldBe(status);
        if (switchToOriginal)
            workspace.Verify(value => value.SetProjectContextAsync(projectId, chatId, original.Id, chatId), Times.Once);
    }

    private static object? Invoke(Home page, string method, params object[] arguments) =>
        typeof(Home).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, arguments);

    private static void Inject(Home page, string property, object value) =>
        typeof(Home).GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);

    private static void SetField(Home page, string field, object value) =>
        typeof(Home).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);

    private static object? GetField(Home page, string field) =>
        typeof(Home).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
}
