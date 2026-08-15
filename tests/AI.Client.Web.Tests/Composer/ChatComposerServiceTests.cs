using AI.Client.Contracts.Chats;
using AI.Client.Contracts.Runs;
using AI.Client.Web.Chats;
using AI.Client.Web.Composer;
using AI.Client.Web.Runs;
using Moq;
using Shouldly;
using Xunit;

namespace AI.Client.Web.Tests.Composer;

/// <summary>
/// Unit tests for <see cref="ChatComposerService"/>.
///
/// Why this exists: the original SubmitChatAsync logic in Home.razor grew into ~80 lines of
/// pre-pause / auto-resume / fork-anchor branching that mixed UI concerns with server
/// orchestration. Several real bugs (queue auto-resuming, duplicate enqueues on a fresh chat,
/// pre-pause running before chat creation) were all the kind that surfaced only when a real
/// user clicked around, and were tedious to reproduce by hand. Pinning the ordering and the
/// rejection conditions in tests makes regressions visible the moment they land.
/// </summary>
public class ChatComposerServiceTests
{
    private readonly Mock<IChatHistoryApi> _chatHistory = new(MockBehavior.Strict);
    private readonly Mock<IChatRunsApi> _chatRuns = new(MockBehavior.Strict);
    private readonly Guid _projectId = Guid.Parse("019f0000-0000-7000-8000-000000000001");
    private readonly Guid _chatId = Guid.Parse("019f0000-0000-7000-8000-000000000002");
    private readonly Guid _runBranchId = Guid.Parse("019f0000-0000-7000-8000-000000000003");
    private readonly DateTimeOffset _now = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ShouldRejectWhenProjectIsNotSelected()
    {
        // Build the request by hand because the Request() helper substitutes a default projectId
        // — which is exactly what this test is verifying the service rejects.
        var request = new ComposerSubmitRequest(
            ComposerSubmitMode.Send,
            ProjectId: null,
            SelectedChat: null,
            BranchLeafId: null,
            ForkSourceId: null,
            ReplaceSourceId: null,
            ReplaceSourceIsGenerating: false,
            CredentialProfileId: null,
            EndpointBaseUrl: "https://api.example.com",
            EndpointModel: "gpt-4",
            Message: "hi",
            SelectedRun: null);

        var outcome = await CreateInstance().SubmitAsync(request, CancellationToken.None);

        outcome.ShouldBeOfType<ComposerSubmitOutcome.Rejected>();
        _chatHistory.VerifyNoOtherCalls();
        _chatRuns.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ShouldRejectWhenMessageIsBlank()
    {
        var outcome = await CreateInstance().SubmitAsync(
            Request(message: "   "),
            CancellationToken.None);

        outcome.ShouldBeOfType<ComposerSubmitOutcome.Rejected>();
        _chatHistory.VerifyNoOtherCalls();
        _chatRuns.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ShouldRejectWhenEndpointIsMissing()
    {
        var outcome = await CreateInstance().SubmitAsync(
            Request(endpointBaseUrl: null, endpointModel: null),
            CancellationToken.None);

        outcome.ShouldBeOfType<ComposerSubmitOutcome.Rejected>();
        _chatHistory.VerifyNoOtherCalls();
        _chatRuns.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ShouldCreateChatThenEnqueueForSendModeOnNewConversation()
    {
        var newChat = NewChat(messages: []);
        var enqueued = IdleSnapshot();
        _chatHistory
            .Setup(i => i.CreateAsync(_projectId, It.Is<CreateChatRequest>(r => r.Title == "Hello"), CancellationToken.None))
            .ReturnsAsync(newChat);
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, newChat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .ReturnsAsync(enqueued);

        var outcome = await CreateInstance().SubmitAsync(
            Request(message: "Hello", selectedChat: null),
            CancellationToken.None);

        var accepted = outcome.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        accepted.Chat.ShouldBe(newChat);
        accepted.HeldInQueue.ShouldBeFalse();
        _chatHistory.Verify(i => i.CreateAsync(_projectId, It.IsAny<CreateChatRequest>(), CancellationToken.None), Times.Once);
        _chatRuns.Verify(i => i.EnqueueAsync(_projectId, newChat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None), Times.Once);
        // Send mode on a brand-new chat must NOT pre-pause — the run is Idle and the user wants
        // it to start immediately. Pre-pausing here was the original "Send just queues" bug.
        _chatRuns.Verify(i => i.StopAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), CancellationToken.None), Times.Never);
    }

    [Fact]
    public async Task ShouldAutoResumeWhenSendModeLandsOnPausedRun()
    {
        var chat = NewChat();
        var paused = PausedSnapshot();
        var resumed = GeneratingSnapshot();
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .ReturnsAsync(paused);
        // ResumeAsync is called with whatever branch id RunBranchIdFor derives from the leaf
        // — for an empty chat it falls back to chat.Id, so we don't pin the value here.
        _chatRuns
            .Setup(i => i.ResumeAsync(_projectId, chat.Id, It.IsAny<Guid>(), It.IsAny<Guid>(), CancellationToken.None))
            .ReturnsAsync(resumed);

        var outcome = await CreateInstance().SubmitAsync(
            Request(selectedChat: chat, selectedRun: paused),
            CancellationToken.None);

        var accepted = outcome.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        accepted.Snapshot.ShouldBe(resumed);
        accepted.HeldInQueue.ShouldBeFalse();
        _chatRuns.Verify(i => i.ResumeAsync(_projectId, chat.Id, It.IsAny<Guid>(), It.IsAny<Guid>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ShouldNotResumeWhenQueueModeLandsOnPausedRun()
    {
        var chat = NewChat();
        var paused = PausedSnapshot();
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .ReturnsAsync(paused);

        var outcome = await CreateInstance().SubmitAsync(
            Request(mode: ComposerSubmitMode.Queue, selectedChat: chat, selectedRun: paused),
            CancellationToken.None);

        var accepted = outcome.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        accepted.HeldInQueue.ShouldBeTrue();
        _chatRuns.Verify(i => i.ResumeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), CancellationToken.None), Times.Never);
    }

    [Fact]
    public async Task ShouldPrePauseIdleRunWhenQueueModeSelected()
    {
        var chat = NewChat();
        var idle = IdleSnapshot();
        var paused = PausedSnapshot();
        // Branch id is whatever RunBranchIdFor derives — for an empty chat with a leaf that
        // doesn't appear in messages that's chat.Id, but pinning it makes the test brittle
        // and the value isn't what we're verifying here.
        _chatRuns
            .Setup(i => i.StopAsync(_projectId, chat.Id, It.IsAny<Guid>(), It.IsAny<Guid>(), CancellationToken.None))
            .ReturnsAsync(paused);
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .ReturnsAsync(paused);

        var outcome = await CreateInstance().SubmitAsync(
            Request(mode: ComposerSubmitMode.Queue, selectedChat: chat, selectedRun: idle),
            CancellationToken.None);

        outcome.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        _chatRuns.Verify(i => i.StopAsync(_projectId, chat.Id, It.IsAny<Guid>(), It.IsAny<Guid>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ShouldPrePauseGeneratingRunWhenQueueModeSelected()
    {
        var chat = NewChat();
        var generating = GeneratingSnapshot();
        var paused = PausedSnapshot();
        _chatRuns
            .Setup(i => i.StopAsync(_projectId, chat.Id, It.IsAny<Guid>(), It.IsAny<Guid>(), CancellationToken.None))
            .ReturnsAsync(paused);
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .ReturnsAsync(paused);

        var outcome = await CreateInstance().SubmitAsync(
            Request(mode: ComposerSubmitMode.Queue, selectedChat: chat, selectedRun: generating),
            CancellationToken.None);

        outcome.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        // Without this pause the worker would have picked the queued message up the moment the
        // current response finished — the original "queue runs immediately on Generating" bug.
        _chatRuns.Verify(i => i.StopAsync(_projectId, chat.Id, It.IsAny<Guid>(), It.IsAny<Guid>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ShouldPrePauseOnBrandNewChatWhenQueueModeSelected()
    {
        // SelectedRun is null here on purpose: that's the state the Home component is in for
        // a chat that was just created and hasn't loaded any run snapshot yet. The old code
        // only paused when SelectedRun was non-null, so Ctrl+Enter on this chat fell straight
        // through to Enqueue, which created an Idle run on the server — and the worker
        // immediately started processing the queued message.
        var newChat = NewChat(messages: []);
        var paused = PausedSnapshot();
        _chatHistory
            .Setup(i => i.CreateAsync(_projectId, It.IsAny<CreateChatRequest>(), CancellationToken.None))
            .ReturnsAsync(newChat);
        _chatRuns
            .Setup(i => i.StopAsync(_projectId, newChat.Id, newChat.Id, It.IsAny<Guid>(), CancellationToken.None))
            .ReturnsAsync(paused);
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, newChat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .ReturnsAsync(paused);

        var outcome = await CreateInstance().SubmitAsync(
            Request(
                mode: ComposerSubmitMode.Queue,
                projectId: _projectId,
                selectedChat: null,
                selectedRun: null),
            CancellationToken.None);

        outcome.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        // The pause MUST happen after the chat is created and must use chat.Id as the branch
        // (there's no run yet, so there's no BranchId to read off the snapshot).
        _chatHistory.Verify(i => i.CreateAsync(_projectId, It.IsAny<CreateChatRequest>(), CancellationToken.None), Times.Once);
        _chatRuns.Verify(i => i.StopAsync(_projectId, newChat.Id, newChat.Id, It.IsAny<Guid>(), CancellationToken.None), Times.Once);
        _chatRuns.Verify(i => i.EnqueueAsync(_projectId, newChat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ShouldNotPrePauseAlreadyPausedRun()
    {
        var chat = NewChat();
        var paused = PausedSnapshot();
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .ReturnsAsync(paused);

        var outcome = await CreateInstance().SubmitAsync(
            Request(mode: ComposerSubmitMode.Queue, selectedChat: chat, selectedRun: paused),
            CancellationToken.None);

        outcome.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        _chatRuns.Verify(i => i.StopAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), CancellationToken.None), Times.Never);
    }

    [Fact]
    public async Task ShouldLeaveParentUnsetWhenQueueingBehindAnotherPendingItem()
    {
        // Regression test (two-stage bug):
        // 1) Queuing a second message before the first queued one has actually run used to send
        //    the SAME ParentMessageId for both (the branch's materialized leaf, which doesn't
        //    advance just because something was queued) — the two became siblings once
        //    materialized.
        // 2) The first fix for that ("chain onto the last queued item's own id") was itself
        //    wrong: a queued item's id is the id its USER message gets, not the id of the
        //    assistant reply that will eventually follow it — chaining onto it made the NEXT
        //    queued item a sibling of that reply instead of following it. Reproduced live: queue
        //    msg1, msg2, msg3 back to back, resume, and msg2/msg3 both showed up as branch points
        //    in the tree because their ParentId pointed at the *user* message before them, not
        //    its reply.
        // The only value that's actually correct here doesn't exist yet (the future reply's id),
        // so this must send null and let ChatRunDispatcher.ProcessAsync's own fallback
        // (queued.ParentMessageId ?? chat.Messages[^1].Id) resolve it — by the time this item is
        // actually processed, the previous item's reply is real and is chat.Messages[^1].
        //
        // NewChat() has no messages and this Request() passes no BranchLeafId, so
        // RunBranchIdFor resolves runBranchId to chat.Id — the queue snapshot's BranchId must
        // match that (not _runBranchId) for the chaining condition to recognize it as this
        // branch's own queue.
        var chat = NewChat();
        var alreadyQueued = new QueuedChatMessage(Guid.Parse("019f0000-0000-7000-8000-0000000000b0"), "first queued", _now, chat.Id);
        var runWithQueue = new ChatRunSnapshot(_projectId, chat.Id, chat.Id, ChatRunStatus.Paused, "", [alreadyQueued], false, null, 1);
        var enqueued = new ChatRunSnapshot(_projectId, chat.Id, chat.Id, ChatRunStatus.Paused, "",
            [alreadyQueued, new QueuedChatMessage(Guid.NewGuid(), "second queued", _now, null)], false, null, 2);
        EnqueueChatMessageRequest? captured = null;
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .Callback<Guid, Guid, EnqueueChatMessageRequest, CancellationToken>((_, _, req, _) => captured = req)
            .ReturnsAsync(enqueued);

        await CreateInstance().SubmitAsync(
            Request(mode: ComposerSubmitMode.Queue, message: "second queued", selectedChat: chat, selectedRun: runWithQueue),
            CancellationToken.None);

        captured.ShouldNotBeNull();
        captured.ParentMessageId.ShouldBeNull();
    }

    [Fact]
    public async Task ShouldNotChainOntoAnotherBranchsQueue()
    {
        // The chaining above must only kick in when the already-queued item belongs to the SAME
        // run branch as this submit — otherwise a queue on one forked branch could leak its
        // anchor into an unrelated branch's message.
        var chat = NewChat();
        var otherBranchId = Guid.Parse("019f0000-0000-7000-8000-0000000000c0");
        var otherBranchQueued = new QueuedChatMessage(Guid.Parse("019f0000-0000-7000-8000-0000000000c1"), "other branch", _now);
        var otherBranchRun = new ChatRunSnapshot(_projectId, chat.Id, otherBranchId, ChatRunStatus.Paused, "", [otherBranchQueued], false, null, 1);
        var enqueued = IdleSnapshot();
        EnqueueChatMessageRequest? captured = null;
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .Callback<Guid, Guid, EnqueueChatMessageRequest, CancellationToken>((_, _, req, _) => captured = req)
            .ReturnsAsync(enqueued);

        await CreateInstance().SubmitAsync(
            // selectedRun belongs to a different branch than the one this submit targets (no
            // BranchLeafId given, so runBranchId resolves to chat.Id — different from otherBranchId).
            Request(selectedChat: chat, selectedRun: otherBranchRun),
            CancellationToken.None);

        captured.ShouldNotBeNull();
        captured.ParentMessageId.ShouldBeNull();
    }

    [Fact]
    public async Task ShouldPrePauseOnExistingChatWithSelectedBranchWhenRunSnapshotMissing()
    {
        // The typical post-SelectChat state: chat exists, a leaf is visible, but the run
        // snapshot for that branch hasn't streamed in yet so SelectedRun is null. The pause
        // must target the same branch the enqueue targets — otherwise the enqueue creates a
        // fresh Idle run on the selected branch's root and the worker happily processes the
        // queued message. The runBranchId for an existing chat with a leaf on the main branch
        // is the chat id; for a forked leaf it's the branch root. Either way, runBranchId is
        // the value the enqueue will use, so pausing on it is always correct.
        var chat = NewChat();
        var paused = PausedSnapshot();
        // NewChat() returns a chat with no messages, so RunBranchIdFor falls back to chat.Id.
        // The setup reflects that exact branch id; the contract being verified is "pause lands
        // on the same branch the enqueue will target", not "pause lands on a specific id".
        _chatRuns
            .Setup(i => i.StopAsync(_projectId, chat.Id, chat.Id, It.IsAny<Guid>(), CancellationToken.None))
            .ReturnsAsync(paused);
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .ReturnsAsync(paused);

        var outcome = await CreateInstance().SubmitAsync(
            Request(
                mode: ComposerSubmitMode.Queue,
                selectedChat: chat,
                branchLeafId: _runBranchId,
                selectedRun: null),
            CancellationToken.None);

        outcome.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        // The bug this guards against: the pause used to land on chat.Id while enqueue ran
        // against _runBranchId, so the new branch's run stayed Idle and the worker started.
        _chatRuns.Verify(i => i.StopAsync(_projectId, chat.Id, chat.Id, It.IsAny<Guid>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ShouldForkFromCurrentLeafOnBareCtrlAltEnterWithNoForkSourceId()
    {
        // Regression test: a bare Ctrl+Alt+Enter on an existing conversation — no preceding
        // "Fork from here"/"Edit and branch" click, so ForkSourceId is still null — must still
        // create a new branch. The fork-anchor shift in Step 3 used to trigger only when
        // ForkSourceId was already set; Mode == Fork alone did nothing, so this exact scenario
        // silently degraded into an ordinary append onto the current branch instead of a fork.
        var leafId = Guid.Parse("019f0000-0000-7000-8000-0000000000d0");
        var parentId = Guid.Parse("019f0000-0000-7000-8000-0000000000d1");
        var leaf = new ChatMessageView(leafId, parentId, "Assistant", "reply", _now);
        var parent = new ChatMessageView(parentId, null, "User", "ask", _now);
        var chat = NewChat(messages: [parent, leaf]);
        var idle = IdleSnapshot();
        var enqueued = IdleSnapshot();
        EnqueueChatMessageRequest? captured = null;
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .Callback<Guid, Guid, EnqueueChatMessageRequest, CancellationToken>((_, _, req, _) => captured = req)
            .ReturnsAsync(enqueued);

        await CreateInstance().SubmitAsync(
            Request(
                mode: ComposerSubmitMode.Fork,
                selectedChat: chat,
                branchLeafId: leafId,
                forkSourceId: null,
                selectedRun: idle),
            CancellationToken.None);

        captured.ShouldNotBeNull();
        // Must land as a sibling of the leaf (child of leaf.ParentId), same structural fact that
        // makes the sidebar show it as a new branch — not appended as leaf's own child.
        captured.ParentMessageId.ShouldBe(parentId);
        captured.BranchId.ShouldBe(captured.MessageId);
    }

    [Fact]
    public async Task ShouldNotPrePauseWhenForkModeSelected()
    {
        // Fork creates a brand new branch rooted at the new message. Pausing that future
        // branchId before enqueue would create an empty Paused run for it, then enqueue
        // would still proceed — and the user would have to press Resume to see a forked
        // conversation start. The right behaviour: let Fork run immediately. The user
        // explicitly asked for a new conversation; making them click Resume on top of that
        // is a UX trap.
        var leafId = Guid.Parse("019f0000-0000-7000-8000-000000000040");
        var parentId = Guid.Parse("019f0000-0000-7000-8000-000000000041");
        var leaf = new ChatMessageView(leafId, parentId, "Assistant", "reply", _now);
        var parent = new ChatMessageView(parentId, null, "User", "ask", _now);
        var chat = NewChat(messages: [parent, leaf]);
        var idle = IdleSnapshot();
        var enqueued = IdleSnapshot();
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .ReturnsAsync(enqueued);

        var outcome = await CreateInstance().SubmitAsync(
            Request(
                mode: ComposerSubmitMode.Fork,
                selectedChat: chat,
                branchLeafId: leafId,
                forkSourceId: leafId,
                selectedRun: idle),
            CancellationToken.None);

        outcome.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        _chatRuns.Verify(i => i.StopAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), CancellationToken.None), Times.Never);
    }

    [Fact]
    public async Task ShouldForkFromLeafParentNotLeafItself()
    {
        var leafId = Guid.Parse("019f0000-0000-7000-8000-000000000010");
        var parentId = Guid.Parse("019f0000-0000-7000-8000-000000000011");
        var leaf = new ChatMessageView(leafId, parentId, "Assistant", "reply", _now);
        var parent = new ChatMessageView(parentId, null, "User", "ask", _now);
        var chat = NewChat(messages: [parent, leaf]);
        var idle = IdleSnapshot();
        var enqueued = IdleSnapshot();
        EnqueueChatMessageRequest? captured = null;
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .Callback<Guid, Guid, EnqueueChatMessageRequest, CancellationToken>((_, _, req, _) => captured = req)
            .ReturnsAsync(enqueued);

        await CreateInstance().SubmitAsync(
            Request(
                mode: ComposerSubmitMode.Fork,
                selectedChat: chat,
                branchLeafId: leafId,
                forkSourceId: leafId,
                selectedRun: idle),
            CancellationToken.None);

        captured.ShouldNotBeNull();
        // The new message must become a sibling of the leaf (= child of leaf.ParentId), not a
        // child of the leaf — that's the structural fact that makes the sidebar treat it as a
        // new branch rather than just appending to the current one.
        captured.ParentMessageId.ShouldBe(parentId);
        captured.BranchId.ShouldNotBe(_runBranchId);
        captured.BranchId.ShouldBe(captured.MessageId);
    }

    [Fact]
    public async Task ShouldTargetChatIdWhenContinuingAnEstablishedMainBranch()
    {
        // Regression test: BranchLeafHelpers.RunBranchIdFor used to walk up from the leaf and,
        // on reaching the message with ParentId == null (which every leaf eventually does — fork
        // or not), return the ORIGINAL leaf id instead of chat.Id. That's wrong for the plain
        // main branch: every run/queue read in Home.razor (GetSelectedRunBranchId) falls back to
        // chat.Id when the leaf isn't part of any BranchTreeItem, so a mismatched runBranchId
        // here meant the run got stored under a key the UI never looks up — Ctrl+Enter silently
        // enqueued into a run nobody could see.
        var userMsg = new ChatMessageView(Guid.Parse("019f0000-0000-7000-8000-000000000090"), null, "User", "hi", _now);
        var replyMsg = new ChatMessageView(Guid.Parse("019f0000-0000-7000-8000-000000000091"), userMsg.Id, "Assistant", "hello", _now.AddSeconds(1));
        var chat = NewChat(messages: [userMsg, replyMsg]);
        var idle = IdleSnapshot();
        var enqueued = IdleSnapshot();
        EnqueueChatMessageRequest? captured = null;
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .Callback<Guid, Guid, EnqueueChatMessageRequest, CancellationToken>((_, _, req, _) => captured = req)
            .ReturnsAsync(enqueued);

        await CreateInstance().SubmitAsync(
            Request(selectedChat: chat, branchLeafId: replyMsg.Id, selectedRun: idle),
            CancellationToken.None);

        captured.ShouldNotBeNull();
        // No sibling anywhere in this chat — this must resolve to the chat's own id, matching
        // what Home.razor's GetSelectedRunBranchId falls back to for a leaf on the main branch.
        captured.BranchId.ShouldBe(chat.Id);
    }

    [Fact]
    public async Task ShouldTargetForkRootWhenContinuingAnEstablishedForkedBranch()
    {
        // Companion regression test to the one above: continuing (not creating) a fork must
        // still resolve to the fork's own anchor id, not chat.Id and not the leaf itself.
        var parent = new ChatMessageView(Guid.Parse("019f0000-0000-7000-8000-0000000000a0"), null, "User", "ask", _now);
        var firstReply = new ChatMessageView(Guid.Parse("019f0000-0000-7000-8000-0000000000a1"), parent.Id, "Assistant", "first", _now.AddSeconds(1));
        // forkRoot is the SECOND (chronologically later) child of `parent` — that's what makes
        // it a fork anchor under the same "non-first sibling" rule Home.razor's
        // ComputeBranchTreeItems uses to identify BranchTreeItem roots.
        var forkRoot = new ChatMessageView(Guid.Parse("019f0000-0000-7000-8000-0000000000a2"), parent.Id, "User", "fork", _now.AddSeconds(2));
        var forkReply = new ChatMessageView(Guid.Parse("019f0000-0000-7000-8000-0000000000a3"), forkRoot.Id, "Assistant", "fork reply", _now.AddSeconds(3));
        var chat = NewChat(messages: [parent, firstReply, forkRoot, forkReply]);
        var idle = IdleSnapshot();
        var enqueued = IdleSnapshot();
        EnqueueChatMessageRequest? captured = null;
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .Callback<Guid, Guid, EnqueueChatMessageRequest, CancellationToken>((_, _, req, _) => captured = req)
            .ReturnsAsync(enqueued);

        await CreateInstance().SubmitAsync(
            Request(selectedChat: chat, branchLeafId: forkReply.Id, selectedRun: idle),
            CancellationToken.None);

        captured.ShouldNotBeNull();
        captured.BranchId.ShouldBe(forkRoot.Id);
    }

    [Fact]
    public async Task ShouldDeleteBranchAndRefreshChatWhenReplaceSourceIsSet()
    {
        var replaceId = Guid.Parse("019f0000-0000-7000-8000-000000000020");
        var parentId = Guid.Parse("019f0000-0000-7000-8000-000000000021");
        var replaceMessage = new ChatMessageView(replaceId, parentId, "User", "old", _now);
        var parentMessage = new ChatMessageView(parentId, null, "User", "ask", _now);
        var chat = NewChat(messages: [parentMessage, replaceMessage], revision: 1);
        var refreshed = NewChat(messages: [parentMessage], revision: 2);
        var enqueued = IdleSnapshot();
        _chatHistory
            .Setup(i => i.DeleteBranchAsync(_projectId, chat.Id, replaceId, 1, CancellationToken.None))
            .ReturnsAsync(new ChatBranchDeleteResult(true, 1, parentId));
        _chatHistory
            .Setup(i => i.GetAsync(_projectId, chat.Id, CancellationToken.None))
            .ReturnsAsync(refreshed);
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .ReturnsAsync(enqueued);

        var outcome = await CreateInstance().SubmitAsync(
            Request(selectedChat: chat, replaceSourceId: replaceId),
            CancellationToken.None);

        var accepted = outcome.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        accepted.Chat.ShouldBe(refreshed);
        _chatHistory.Verify(i => i.DeleteBranchAsync(_projectId, chat.Id, replaceId, 1, CancellationToken.None), Times.Once);
        _chatHistory.Verify(i => i.GetAsync(_projectId, chat.Id, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ShouldRejectWhenReplaceBranchDeleteReturnsFalse()
    {
        var replaceId = Guid.Parse("019f0000-0000-7000-8000-000000000030");
        var replaceMessage = new ChatMessageView(replaceId, null, "User", "old", _now);
        var chat = NewChat(messages: [replaceMessage], revision: 1);
        _chatHistory
            .Setup(i => i.DeleteBranchAsync(_projectId, chat.Id, replaceId, 1, CancellationToken.None))
            .ReturnsAsync(new ChatBranchDeleteResult(false, 1, null));

        var outcome = await CreateInstance().SubmitAsync(
            Request(selectedChat: chat, replaceSourceId: replaceId),
            CancellationToken.None);

        outcome.ShouldBeOfType<ComposerSubmitOutcome.Rejected>();
        // Crucially we must NOT proceed to enqueue when the delete failed — the message would
        // be attached to a now-inconsistent branch.
        _chatRuns.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ShouldStillEnqueueWhenPrePauseFailsWithHttpException()
    {
        var chat = NewChat();
        var idle = IdleSnapshot();
        // NewChat() returns a chat with no messages; RunBranchIdFor falls back to chat.Id.
        _chatRuns
            .Setup(i => i.StopAsync(_projectId, chat.Id, chat.Id, It.IsAny<Guid>(), CancellationToken.None))
            .ThrowsAsync(new HttpRequestException("gateway down"));
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .ReturnsAsync(idle);

        var outcome = await CreateInstance().SubmitAsync(
            Request(mode: ComposerSubmitMode.Queue, selectedChat: chat, selectedRun: idle),
            CancellationToken.None);

        // Pre-pause is best-effort. If the network blip takes it out, the enqueue must still
        // happen — otherwise the user loses their typed message.
        outcome.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        _chatRuns.Verify(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ShouldStillEnqueueWhenResumeFailsWithHttpException()
    {
        var chat = NewChat();
        var paused = PausedSnapshot();
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .ReturnsAsync(paused);
        _chatRuns
            .Setup(i => i.ResumeAsync(_projectId, chat.Id, It.IsAny<Guid>(), It.IsAny<Guid>(), CancellationToken.None))
            .ThrowsAsync(new HttpRequestException("gateway down"));

        var outcome = await CreateInstance().SubmitAsync(
            Request(selectedChat: chat, selectedRun: paused),
            CancellationToken.None);

        // Same logic for resume: it's a UX nicety, not a correctness requirement. If the call
        // fails the message is still queued and the user can press Resume manually.
        outcome.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
    }

    [Fact]
    public async Task ShouldReportNetworkErrorWhenEnqueueThrowsHttpException()
    {
        var chat = NewChat();
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var outcome = await CreateInstance().SubmitAsync(
            Request(selectedChat: chat),
            CancellationToken.None);

        var rejected = outcome.ShouldBeOfType<ComposerSubmitOutcome.Rejected>();
        rejected.Reason.ShouldContain("endpoint");
    }

    // ===== Matrix tests =====
    //
    // Each row of the matrix below corresponds to a (state, mode) pair from the state x action
    // table in docs/15-composer-rules.md — that doc, not this comment, is the source of truth;
    // update it first when a rule changes, then bring these tests in line with it.
    // Naming convention: Should_DoX_In_StateY_OnActionZ.
    // They share helpers above for snapshots/chats so adding new rows is cheap.

    [Theory]
    [InlineData(ChatRunStatus.Idle, false)]
    [InlineData(ChatRunStatus.Generating, false)]
    [InlineData(ChatRunStatus.Completed, false)]
    [InlineData(ChatRunStatus.Paused, false)]
    [InlineData(ChatRunStatus.Interrupted, false)]
    [InlineData(ChatRunStatus.Failed, false)]
    public async Task SendMatchesExpectedHeldStateAcrossRunStatuses(ChatRunStatus status, bool expectedHeld)
    {
        // S1..S7: Send mode on an existing chat. The contract is "after submit, is the
        // message still in queue (not running)?" — Idle/Generating/Completed already start
        // the new message immediately on Enqueue (worker picks it up), so held=false.
        // Paused/Interrupted/Failed would normally hold the message, but Enter (Send)
        // explicitly triggers ResumeAsync, so a successful resume means held=false too.
        var chat = NewChat();
        var run = new ChatRunSnapshot(_projectId, chat.Id, _runBranchId, status, "", [], false, null, 1);
        var enqueued = new ChatRunSnapshot(_projectId, chat.Id, _runBranchId, ChatRunStatus.Idle, "",
            [new QueuedChatMessage(Guid.NewGuid(), "hi", _now)], false, null, 2);
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .ReturnsAsync(enqueued);
        if (status is ChatRunStatus.Paused or ChatRunStatus.Interrupted or ChatRunStatus.Failed)
        {
            _chatRuns
                .Setup(i => i.ResumeAsync(_projectId, chat.Id, It.IsAny<Guid>(), It.IsAny<Guid>(), CancellationToken.None))
                .ReturnsAsync(new ChatRunSnapshot(_projectId, chat.Id, _runBranchId, ChatRunStatus.Generating, "",
                    [new QueuedChatMessage(Guid.NewGuid(), "hi", _now)], false, null, 3));
        }

        var outcome = await CreateInstance().SubmitAsync(
            Request(selectedChat: chat, selectedRun: run),
            CancellationToken.None);

        var accepted = outcome.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        // Enter (Send) always auto-resumes a held run, so the final snapshot is generating/idle
        // and the message is no longer "held in queue".
        accepted.HeldInQueue.ShouldBe(expectedHeld);
    }

    [Theory]
    [InlineData(ChatRunStatus.Paused)]
    [InlineData(ChatRunStatus.Interrupted)]
    [InlineData(ChatRunStatus.Failed)]
    public async Task SendKeepsMessageHeldWhenResumeFails(ChatRunStatus status)
    {
        // If the ResumeAsync call after Enter fails, the message must stay held in the queue
        // so the user can retry rather than losing the input silently.
        var chat = NewChat();
        var run = new ChatRunSnapshot(_projectId, chat.Id, _runBranchId, status, "", [], false, null, 1);
        var enqueued = new ChatRunSnapshot(_projectId, chat.Id, _runBranchId, status, "",
            [new QueuedChatMessage(Guid.NewGuid(), "hi", _now)], false, null, 2);
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .ReturnsAsync(enqueued);
        _chatRuns
            .Setup(i => i.ResumeAsync(_projectId, chat.Id, It.IsAny<Guid>(), It.IsAny<Guid>(), CancellationToken.None))
            .ThrowsAsync(new HttpRequestException("network down"));

        var outcome = await CreateInstance().SubmitAsync(
            Request(selectedChat: chat, selectedRun: run),
            CancellationToken.None);

        var accepted = outcome.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        accepted.HeldInQueue.ShouldBeTrue();
    }

    [Theory]
    [InlineData(ChatRunStatus.Idle)]
    [InlineData(ChatRunStatus.Generating)]
    [InlineData(ChatRunStatus.Completed)]
    [InlineData(ChatRunStatus.Paused)]
    [InlineData(ChatRunStatus.Interrupted)]
    [InlineData(ChatRunStatus.Failed)]
    public async Task QueueAlwaysHoldsRegardlessOfRunStatus(ChatRunStatus status)
    {
        // Ctrl+Enter on any run status must NOT start the worker. The held flag is what tells
        // the UI to show "Stopped · queue paused" and the Resume button — without it the user
        // would have typed Ctrl+Enter and watched the message run anyway.
        var chat = NewChat();
        var run = new ChatRunSnapshot(_projectId, chat.Id, _runBranchId, status, "", [], false, null, 1);
        var enqueued = new ChatRunSnapshot(_projectId, chat.Id, _runBranchId,
            status is ChatRunStatus.Paused or ChatRunStatus.Interrupted or ChatRunStatus.Failed ? status : ChatRunStatus.Paused,
            "", [new QueuedChatMessage(Guid.NewGuid(), "hi", _now)], false, null, 2);
        _chatRuns
            .Setup(i => i.StopAsync(_projectId, chat.Id, It.IsAny<Guid>(), It.IsAny<Guid>(), CancellationToken.None))
            .ReturnsAsync(enqueued);
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .ReturnsAsync(enqueued);

        var outcome = await CreateInstance().SubmitAsync(
            Request(mode: ComposerSubmitMode.Queue, selectedChat: chat, selectedRun: run),
            CancellationToken.None);

        var accepted = outcome.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        accepted.HeldInQueue.ShouldBeTrue();
    }

    [Theory]
    [InlineData(ChatRunStatus.Idle)]
    [InlineData(ChatRunStatus.Generating)]
    [InlineData(ChatRunStatus.Paused)]
    public async Task ForkAlwaysRunsImmediatelyRegardlessOfRunStatus(ChatRunStatus status)
    {
        // Fork is an explicit "start a new conversation" action. Pausing the run on top of
        // that would force the user to press Resume just to see their fork start — wrong.
        var leafId = Guid.Parse("019f0000-0000-7000-8000-000000000050");
        var parentId = Guid.Parse("019f0000-0000-7000-8000-000000000051");
        var leaf = new ChatMessageView(leafId, parentId, "Assistant", "reply", _now);
        var parent = new ChatMessageView(parentId, null, "User", "ask", _now);
        var chat = NewChat(messages: [parent, leaf]);
        var run = new ChatRunSnapshot(_projectId, chat.Id, _runBranchId, status, "", [], false, null, 1);
        var enqueued = new ChatRunSnapshot(_projectId, chat.Id, _runBranchId, ChatRunStatus.Idle, "", [], false, null, 2);
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .ReturnsAsync(enqueued);

        var outcome = await CreateInstance().SubmitAsync(
            Request(
                mode: ComposerSubmitMode.Fork,
                selectedChat: chat,
                branchLeafId: leafId,
                selectedRun: run),
            CancellationToken.None);

        var accepted = outcome.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        accepted.HeldInQueue.ShouldBeFalse();
    }

    [Fact]
    public async Task QueueIntoForkedBranchWhenForkSourceIdSetOnSend()
    {
        // The post-EditAndBranch / post-ForkFromMessage state: ForkSourceId is set but the
        // user pressed Enter (not Ctrl+Alt+Enter). The new message must still anchor at the
        // leaf's parent — that's the difference between "queue into the forked session" and
        // "queue into the current branch" (the latter would contradict the fork).
        var leafId = Guid.Parse("019f0000-0000-7000-8000-000000000060");
        var parentId = Guid.Parse("019f0000-0000-7000-8000-000000000061");
        var leaf = new ChatMessageView(leafId, parentId, "Assistant", "reply", _now);
        var parent = new ChatMessageView(parentId, null, "User", "ask", _now);
        var chat = NewChat(messages: [parent, leaf]);
        var idle = IdleSnapshot();
        var enqueued = IdleSnapshot();
        EnqueueChatMessageRequest? captured = null;
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .Callback<Guid, Guid, EnqueueChatMessageRequest, CancellationToken>((_, _, req, _) => captured = req)
            .ReturnsAsync(enqueued);

        await CreateInstance().SubmitAsync(
            Request(
                mode: ComposerSubmitMode.Send,
                selectedChat: chat,
                branchLeafId: leafId,
                forkSourceId: leafId,
                selectedRun: idle),
            CancellationToken.None);

        captured.ShouldNotBeNull();
        // Fork-shift must apply whenever ForkSourceId is set, regardless of mode — otherwise
        // Send/Queue on a forked composer would silently attach to the wrong branch.
        captured.ParentMessageId.ShouldBe(parentId);
        captured.BranchId.ShouldNotBe(_runBranchId);
    }

    [Fact]
    public async Task QueueIntoForkedBranchWhenForkSourceIdSetOnQueue()
    {
        // Same as the Send variant above but with Ctrl+Enter. The bug used to gate the fork
        // shift on mode == Fork, so Queue on a forked composer landed in the original
        // (now-orphaned) branch instead of the user's intended fork.
        var leafId = Guid.Parse("019f0000-0000-7000-8000-000000000070");
        var parentId = Guid.Parse("019f0000-0000-7000-8000-000000000071");
        var leaf = new ChatMessageView(leafId, parentId, "Assistant", "reply", _now);
        var parent = new ChatMessageView(parentId, null, "User", "ask", _now);
        var chat = NewChat(messages: [parent, leaf]);
        var idle = IdleSnapshot();
        var paused = PausedSnapshot();
        EnqueueChatMessageRequest? captured = null;
        _chatRuns
            .Setup(i => i.StopAsync(_projectId, chat.Id, It.IsAny<Guid>(), It.IsAny<Guid>(), CancellationToken.None))
            .ReturnsAsync(paused);
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .Callback<Guid, Guid, EnqueueChatMessageRequest, CancellationToken>((_, _, req, _) => captured = req)
            .ReturnsAsync(paused);

        await CreateInstance().SubmitAsync(
            Request(
                mode: ComposerSubmitMode.Queue,
                selectedChat: chat,
                branchLeafId: leafId,
                forkSourceId: leafId,
                selectedRun: idle),
            CancellationToken.None);

        captured.ShouldNotBeNull();
        captured.ParentMessageId.ShouldBe(parentId);
    }

    [Fact]
    public async Task NewChatSendCreatesChatEnqueuesAndRunsImmediately()
    {
        // S0 (Send): no chat, no run. Service creates the chat, enqueues, and the run is
        // ready to start — the worker picks it up because the new run is Idle.
        var newChat = NewChat(messages: []);
        var enqueued = new ChatRunSnapshot(_projectId, newChat.Id, newChat.Id, ChatRunStatus.Idle, "",
            [new QueuedChatMessage(Guid.NewGuid(), "hi", _now)], false, null, 1);
        _chatHistory
            .Setup(i => i.CreateAsync(_projectId, It.IsAny<CreateChatRequest>(), CancellationToken.None))
            .ReturnsAsync(newChat);
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, newChat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .ReturnsAsync(enqueued);

        var outcome = await CreateInstance().SubmitAsync(
            Request(mode: ComposerSubmitMode.Send, projectId: _projectId, selectedChat: null, selectedRun: null),
            CancellationToken.None);

        var accepted = outcome.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        accepted.Chat.ShouldBe(newChat);
        accepted.HeldInQueue.ShouldBeFalse();
        // No pre-pause on Send.
        _chatRuns.Verify(i => i.StopAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), CancellationToken.None), Times.Never);
    }

    [Fact]
    public async Task NewChatQueueCreatesChatPrePausesThenEnqueues()
    {
        // S0 (Queue): no chat, no run. Service creates the chat, pre-pauses the brand-new run,
        // enqueues into it. The worker stays idle until Resume — this is the exact scenario
        // the bug was originally reported on ("Ctrl+Enter on a fresh chat runs immediately").
        var newChat = NewChat(messages: []);
        var paused = new ChatRunSnapshot(_projectId, newChat.Id, newChat.Id, ChatRunStatus.Paused, "",
            [new QueuedChatMessage(Guid.NewGuid(), "hi", _now)], false, null, 2);
        _chatHistory
            .Setup(i => i.CreateAsync(_projectId, It.IsAny<CreateChatRequest>(), CancellationToken.None))
            .ReturnsAsync(newChat);
        _chatRuns
            .Setup(i => i.StopAsync(_projectId, newChat.Id, newChat.Id, It.IsAny<Guid>(), CancellationToken.None))
            .ReturnsAsync(new ChatRunSnapshot(_projectId, newChat.Id, newChat.Id, ChatRunStatus.Paused, "", [], false, null, 1));
        _chatRuns
            .Setup(i => i.EnqueueAsync(_projectId, newChat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
            .ReturnsAsync(paused);

        var outcome = await CreateInstance().SubmitAsync(
            Request(mode: ComposerSubmitMode.Queue, projectId: _projectId, selectedChat: null, selectedRun: null),
            CancellationToken.None);

        var accepted = outcome.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
        accepted.HeldInQueue.ShouldBeTrue();
        // Pause and enqueue both target chat.Id (= runBranchId for a brand-new chat).
        _chatRuns.Verify(i => i.StopAsync(_projectId, newChat.Id, newChat.Id, It.IsAny<Guid>(), CancellationToken.None), Times.Once);
        _chatRuns.Verify(i => i.EnqueueAsync(_projectId, newChat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task AcceptedOutcomeAlwaysIncludesQueueInSnapshot()
    {
        // The UI renders the queue from accepted.Snapshot. If the snapshot's Queue is empty
        // when the worker hasn't yet received a stream update, the queue UI won't appear.
        // Verify the Accepted outcome always carries the new item regardless of mode.
        foreach (var mode in new[] { ComposerSubmitMode.Send, ComposerSubmitMode.Queue, ComposerSubmitMode.Fork })
        {
            // Fresh mocks per iteration: Queue mode additionally calls StopAsync (pre-pause on
            // an Idle selected run), which a single shared strict mock across all three modes
            // would need a conditional setup for — separate instances keep each iteration's
            // expectations legible.
            var chatHistory = new Mock<IChatHistoryApi>(MockBehavior.Strict);
            var chatRuns = new Mock<IChatRunsApi>(MockBehavior.Strict);
            var chat = NewChat();
            var queuedItem = new QueuedChatMessage(Guid.NewGuid(), "msg", _now);
            var snapshot = new ChatRunSnapshot(_projectId, chat.Id, _runBranchId, ChatRunStatus.Idle, "",
                [queuedItem], false, null, 1);
            if (mode == ComposerSubmitMode.Queue)
            {
                chatRuns
                    .Setup(i => i.StopAsync(_projectId, chat.Id, It.IsAny<Guid>(), It.IsAny<Guid>(), CancellationToken.None))
                    .ReturnsAsync(snapshot);
            }
            chatRuns
                .Setup(i => i.EnqueueAsync(_projectId, chat.Id, It.IsAny<EnqueueChatMessageRequest>(), CancellationToken.None))
                .ReturnsAsync(snapshot);

            var outcome = await new ChatComposerService(chatHistory.Object, chatRuns.Object).SubmitAsync(
                Request(mode: mode, selectedChat: chat, selectedRun: IdleSnapshot()),
                CancellationToken.None);

            var accepted = outcome.ShouldBeOfType<ComposerSubmitOutcome.Accepted>();
            accepted.Snapshot.Queue.ShouldContain(queuedItem);
        }
    }

    [Fact]
    public async Task ShouldRejectReplaceWhenTargetBranchIsGenerating()
    {
        // The button that starts this flow (MessageFeed's "Edit and replace branch") is
        // disabled while SelectedRun.Status == Generating, and Home.razor re-derives the same
        // flag right before submit — this pins the service-level gate both of those rely on.
        var replaceId = Guid.Parse("019f0000-0000-7000-8000-000000000080");
        var chat = NewChat(messages: [new ChatMessageView(replaceId, null, "User", "old", _now)]);

        var outcome = await CreateInstance().SubmitAsync(
            Request(selectedChat: chat, replaceSourceId: replaceId, replaceSourceIsGenerating: true),
            CancellationToken.None);

        var rejected = outcome.ShouldBeOfType<ComposerSubmitOutcome.Rejected>();
        rejected.Reason.ShouldContain("generat");
        // Must reject before touching the branch at all — deleting it here would discard the
        // in-flight response with no way back.
        _chatHistory.VerifyNoOtherCalls();
        _chatRuns.VerifyNoOtherCalls();
    }

    private ChatComposerService CreateInstance() => new(_chatHistory.Object, _chatRuns.Object);

    private ComposerSubmitRequest Request(
        ComposerSubmitMode mode = ComposerSubmitMode.Send,
        Guid? projectId = null,
        ChatDetails? selectedChat = null,
        Guid? branchLeafId = null,
        Guid? replaceSourceId = null,
        bool replaceSourceIsGenerating = false,
        Guid? forkSourceId = null,
        string message = "hi",
        string? endpointBaseUrl = "https://api.example.com",
        string? endpointModel = "gpt-4",
        ChatRunSnapshot? selectedRun = null)
    {
        // No defaults for nullable fields — tests that want "no chat" pass null and expect the
        // service to call CreateAsync. Substituting a default chat here would silently make
        // those tests pass for the wrong reason (the service would see an existing chat and
        // skip the create path).
        projectId ??= _projectId;
        return new ComposerSubmitRequest(
            mode,
            projectId,
            selectedChat,
            branchLeafId,
            forkSourceId,
            replaceSourceId,
            replaceSourceIsGenerating,
            null,
            endpointBaseUrl,
            endpointModel,
            message,
            selectedRun);
    }

    private ChatDetails NewChat(IReadOnlyList<ChatMessageView>? messages = null, long revision = 1) =>
        new(_chatId, _projectId, "New", _now, _now, revision, null, messages ?? []);

    private ChatRunSnapshot IdleSnapshot() =>
        new(_projectId, _chatId, _runBranchId, ChatRunStatus.Idle, "", [], false, null, 1);

    private ChatRunSnapshot GeneratingSnapshot() =>
        new(_projectId, _chatId, _runBranchId, ChatRunStatus.Generating, "", [], false, null, 1);

    private ChatRunSnapshot PausedSnapshot() =>
        new(_projectId, _chatId, _runBranchId, ChatRunStatus.Paused, "", [], false, null, 1);
}
