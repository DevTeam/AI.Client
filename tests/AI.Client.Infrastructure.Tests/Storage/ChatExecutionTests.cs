namespace AI.Client.Infrastructure.Tests.Storage;

using AI.Client.Application.Chat;
using Application.Chats;
using AI.Client.Application.Projects;
using Application.Runs;
using AI.Client.Application.Settings;
using AI.Client.Contracts.Chat;
using Contracts.Chats;
using AI.Client.Contracts.Projects;
using Contracts.Runs;
using AI.Client.Contracts.Settings;
using Projects;
using Settings;
using AI.Client.Infrastructure.Storage;
using Moq;
using Shouldly;
using Xunit;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AI.Client.Application.Tools;
using AI.Client.Application.Workspace;
using AI.Client.Contracts.Tools;
using AI.Client.Contracts.Workspace;
using AI.Client.Infrastructure.Workspace;
using System.Text.Json;

public sealed class ChatExecutionTests
{
    [Fact]
    public async Task WorkspaceChangesShouldBeLiveBeforeBecomingPartOfTheFinalReply()
    {
        var workspace = new TestWorkspaceChangeTracker();
        var changes = new WorkspaceChangeSet(
            [new FileChange("live.cs", FileChangeKind.Modified, 1, 1, Diff: "live diff")], 1, 1);
        workspace.Enqueue(changes);
        await using var fixture = await Fixture.CreateAsync(workspace);
        await fixture.SetPolicyAsync("Allow");

        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Change a file"));
        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        first.Answer.SetResult("");
        var final = await fixture.NextCallAsync();

        var live = await fixture.WaitAsync(run => run.Status == ChatRunStatus.Generating
            && run.WorkspaceChanges is { IsEmpty: false });
        live.WorkspaceChanges!.Files.ShouldHaveSingleItem().Path.ShouldBe("live.cs");

        final.Answer.SetResult("Done");
        var completed = await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        completed.WorkspaceChanges.ShouldBeNull();
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Single(message => message.Content == "Done")
            .WorkspaceChanges!.Files.ShouldHaveSingleItem().Path.ShouldBe("live.cs");
    }

    [Fact]
    public async Task CompletedRepliesShouldKeepTheirOwnWorkspaceChangesAfterRestart()
    {
        var workspace = new TestWorkspaceChangeTracker();
        await using var fixture = await Fixture.CreateAsync(workspace);
        workspace.Enqueue(new WorkspaceChangeSet(
            [new FileChange("first.cs", FileChangeKind.Modified, 2, 1, Diff: "first diff")], 2, 1));
        workspace.Enqueue(new WorkspaceChangeSet(
            [new FileChange("second.cs", FileChangeKind.Added, 3, 0, Diff: "second diff")], 3, 0));

        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "First"));
        (await fixture.NextCallAsync()).Answer.SetResult("First reply");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);

        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Second"));
        (await fixture.NextCallAsync()).Answer.SetResult("Second reply");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        await fixture.RestartAsync();

        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        var replies = chat!.Messages.Where(message => message.Role == "Assistant").ToArray();
        replies.Length.ShouldBe(2);
        replies[0].WorkspaceChanges!.Files.ShouldHaveSingleItem().Path.ShouldBe("first.cs");
        replies[0].WorkspaceChanges!.Additions.ShouldBe(2);
        replies[1].WorkspaceChanges!.Files.ShouldHaveSingleItem().Path.ShouldBe("second.cs");
        replies[1].WorkspaceChanges!.Additions.ShouldBe(3);
    }

    [Theory]
    [InlineData(ToolApprovalAction.AllowForChat)]
    [InlineData(ToolApprovalAction.AllowForProject)]
    [InlineData(ToolApprovalAction.AllowGlobally)]
    public async Task ScopedApprovalShouldPersistAtTheSelectedLevel(ToolApprovalAction action)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Run command"));
        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        first.Answer.SetResult("");
        var pending = await fixture.WaitAsync(run => run.PendingApproval is not null);
        (await fixture.Dispatcher.DecideToolAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId,
            new ToolApprovalDecision(pending.PendingApproval!.Id, action), CancellationToken.None)).ShouldBeTrue();
        var second = await fixture.NextCallAsync();
        second.Answer.SetResult("Done");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);

        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        var project = await fixture.GetProjectAsync();
        var global = await fixture.GetGlobalAsync();
        (chat!.ToolPolicies?.Count ?? 0).ShouldBe(action == ToolApprovalAction.AllowForChat ? 1 : 0);
        project!.ToolPolicies.Count.ShouldBe(action == ToolApprovalAction.AllowForProject ? 1 : 0);
        global.ToolPolicies.Count.ShouldBe(action == ToolApprovalAction.AllowGlobally ? 1 : 0);
    }

    [Fact]
    public async Task GlobalToolPolicyShouldApplyWithoutProjectSettings()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetGlobalPolicyAsync("Allow");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Run command"));
        var first = await fixture.NextCallAsync();
        first.Request.Tools!.Count.ShouldBe(1);
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        first.Answer.SetResult("");
        var second = await fixture.NextCallAsync();
        fixture.Tools.CallCount.ShouldBe(1);
        second.Answer.SetResult("Done");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
    }

    [Theory]
    [InlineData("Allow", 1)]
    [InlineData("Deny", 0)]
    public async Task ShouldRespectPoliciesWithoutPrompting(string decision, int expectedCalls)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetPolicyAsync(decision);
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Run command"));
        var first = await fixture.NextCallAsync();
        first.Request.Tools!.Count.ShouldBe(decision == "Deny" ? 0 : 1);
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        first.Answer.SetResult("");
        var second = await fixture.NextCallAsync();
        fixture.Tools.CallCount.ShouldBe(expectedCalls);
        second.Answer.SetResult("Done");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
    }

    [Fact]
    public async Task RevokedPolicyWhileWaitingForApprovalMustPreventExecution()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetPolicyAsync("Ask");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Run command"));
        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        first.Answer.SetResult("");
        var pending = await fixture.WaitAsync(run => run.PendingApproval is not null);
        await fixture.SetPolicyAsync("Deny");
        await fixture.Dispatcher.DecideToolAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId,
            new ToolApprovalDecision(pending.PendingApproval!.Id, ToolApprovalAction.Allow), CancellationToken.None);
        var second = await fixture.NextCallAsync();
        fixture.Tools.CallCount.ShouldBe(0);
        second.Answer.SetResult("Denied");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ToolMustWaitForApprovalAndPersistTheExchange(bool allow)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetPolicyAsync("Ask");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Run command"));
        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        first.Answer.SetResult("");
        var pending = await fixture.WaitAsync(run => run.PendingApproval is not null);
        fixture.Tools.CallCount.ShouldBe(0);
        var before = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        before!.Messages.Single(message => message.ToolCalls is not null).ToolCalls![0].Id.ShouldBe("call-1");
        (await fixture.Dispatcher.DecideToolAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId,
            new ToolApprovalDecision(Guid.NewGuid(), ToolApprovalAction.Allow), CancellationToken.None)).ShouldBeFalse();
        (await fixture.Dispatcher.DecideToolAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId,
            new ToolApprovalDecision(pending.PendingApproval!.Id, allow ? ToolApprovalAction.Allow : ToolApprovalAction.Deny), CancellationToken.None)).ShouldBeTrue();
        var second = await fixture.NextCallAsync();
        second.Request.ContextMessages![^1].ToolCallId.ShouldBe("call-1");
        fixture.Tools.CallCount.ShouldBe(allow ? 1 : 0);
        second.Answer.SetResult("Final response");
        var completed = await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        await fixture.RestartAsync();
        var restored = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        restored!.Messages.Single(message => message.Id == completed.HeadMessageId).Content.ShouldBe("Final response");
        ChatContext.Get(restored, completed.HeadMessageId!.Value).Select(message => message.Role).ShouldBe(["user", "assistant", "tool", "assistant"]);
        fixture.Tools.CallCount.ShouldBe(allow ? 1 : 0);
    }

    [Fact]
    public async Task ShouldOpenAToolSessionEvenThoughToolsCanStartNestedRuns()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetPolicyAsync("Allow");

        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Anything"));
        (await fixture.NextCallAsync()).Answer.SetResult("Done");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);

        // A tool that can start a nested run makes the agent depend on the tools and the tools
        // depend on the agent. Asking for the session factory by instance closed that loop during
        // construction and produced an agent holding null, which surfaced only here — as a bare
        // "Object reference not set" the moment a run started.
        fixture.Tools.OpenCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task GrantingTheToolInSettingsMustReleaseAWaitingApproval()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetPolicyAsync("Ask");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Run command"));
        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        first.Answer.SetResult("");
        await fixture.WaitAsync(run => run.PendingApproval is not null);

        // Answering from settings rather than from the card is still answering. Before this, the
        // prompt kept waiting on a policy that already said yes, and the run died on its timeout.
        await fixture.SetPolicyAsync("Allow");

        var second = await fixture.NextCallAsync();
        fixture.Tools.CallCount.ShouldBe(1);
        second.Answer.SetResult("Done");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
    }

    [Fact]
    public async Task ATimedOutCallMustNotTakeTheRestOfTheBatchWithIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        // One second of patience per call, so the first one's timeout arrives quickly.
        await fixture.SetPolicyAsync("Allow", timeoutSeconds: 1);
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Run two commands"));
        var first = await fixture.NextCallAsync();
        first.ToolCalls =
        [
            new ChatToolCall("call-1", "mcp_built_in__process_run", "{}"),
            new ChatToolCall("call-2", "mcp_built_in__process_run", "{}"),
        ];
        fixture.Tools.HangNextCall = true;
        first.Answer.SetResult("");

        // The second call must still run: one unresponsive server is that call's failure, not the
        // turn's. Before this, its timeout ended the whole run and every later call was abandoned.
        var second = await fixture.NextCallAsync();
        second.Answer.SetResult("Done");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);

        fixture.Tools.CallCount.ShouldBe(2);
        var answered = await fixture.WaitForToolAnswersAsync(2);
        answered.ShouldBe(["call-1", "call-2"], ignoreOrder: true);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Single(message => message.ToolCallId == "call-1").Content.ShouldContain("went silent for");
    }

    [Fact]
    public async Task AnEmptyTurnShouldBeAskedAgainRatherThanEndTheRun()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Question"));
        // Nothing at all: no text, no tool calls. Nothing is persisted for such a turn, so the
        // retry sends the same request — and a run that had already done work used to lose it.
        (await fixture.NextCallAsync()).Answer.SetResult("");
        (await fixture.NextCallAsync()).Answer.SetResult("Reply");

        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Select(message => message.Content).ShouldBe(["Question", "Reply"]);
    }

    [Fact]
    public async Task AnEndpointThatKeepsAnsweringNothingShouldStillFailTheRun()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Question"));
        // Retrying is for a hiccup, not for an endpoint that has nothing to say: the ceiling is
        // what keeps the user from waiting through attempt after attempt for the same silence.
        for (var attempt = 0; attempt < 3; attempt++) (await fixture.NextCallAsync()).Answer.SetResult("");

        (await fixture.WaitAsync(run => run.Status == ChatRunStatus.Failed)).Error.ShouldNotBeNull().ShouldContain("empty response");
    }

    [Fact]
    public async Task AToolThatKeepsReportingMustNotBeKilledForOutLastingOneCallsPatience()
    {
        await using var fixture = await Fixture.CreateAsync();
        // A second of silence is all this policy allows — and the call takes three times that.
        await fixture.SetPolicyAsync("Allow", timeoutSeconds: 1);
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Delegate some work"));
        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        fixture.Tools.ReportNextCallFor = TimeSpan.FromSeconds(3);
        first.Answer.SetResult("");

        var second = await fixture.NextCallAsync();
        second.Answer.SetResult("Done");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);

        // The timeout measures silence, not duration. A fan-out of subtasks reports what each of
        // them is doing throughout, and killing it at the per-call timeout threw that work away.
        await fixture.WaitForToolAnswersAsync(1);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Single(message => message.ToolCallId == "call-1").Content.ShouldNotContain("went silent");
    }

    [Fact]
    public async Task StoppingOneCallMustStillAnswerTheRestOfItsBatch()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetPolicyAsync("Ask");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Run three commands"));
        var first = await fixture.NextCallAsync();
        first.ToolCalls =
        [
            new ChatToolCall("call-1", "mcp_built_in__process_run", "{}"),
            new ChatToolCall("call-2", "mcp_built_in__process_run", "{}"),
            new ChatToolCall("call-3", "mcp_built_in__process_run", "{}"),
        ];
        first.Answer.SetResult("");
        await fixture.WaitAsync(run => run.PendingApproval is not null);
        await fixture.Dispatcher.StopAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None);
        await fixture.WaitAsync(run => run.Status != ChatRunStatus.Generating);

        // Every tool call the model made must end up with an answer, or the stored history is one
        // the endpoint rejects and the run can never be resumed from. Stopping publishes the paused
        // state before the agent has finished unwinding, so the answers are waited for rather than
        // read the instant the status flips.
        var answered = await fixture.WaitForToolAnswersAsync(3);
        answered.ShouldBe(["call-1", "call-2", "call-3"], ignoreOrder: true);
        fixture.Tools.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task StoppedApprovalMustNotExecuteOnRestart()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetPolicyAsync("Ask");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Run command"));
        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        first.Answer.SetResult("");
        await fixture.WaitAsync(run => run.PendingApproval is not null);
        await fixture.Dispatcher.StopAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None);
        await fixture.RestartAsync();
        await fixture.Dispatcher.ResumeAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None);
        var resumed = await fixture.NextCallAsync();
        fixture.Tools.CallCount.ShouldBe(0);
        resumed.Request.ContextMessages![^1].Content.ShouldContain("interrupted", Case.Insensitive);
        resumed.Answer.SetResult("Stopped");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
    }
    [Fact]
    public async Task QueueShouldRemainPausedAndRepeatedSubmitShouldHaveNoSecondEffect()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Queued", ChatSubmitMode.Queue);
        var first = await fixture.SubmitAsync(request);
        var second = await fixture.SubmitAsync(request);
        second.ShouldBe(first);
        second.Status.ShouldBe(ChatRunStatus.Paused);
        second.Queue.ShouldHaveSingleItem();
        fixture.Completion.Calls.Reader.TryRead(out _).ShouldBeFalse();
        await fixture.Dispatcher.ResumeAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None);
        var call = await fixture.NextCallAsync();
        call.Request.Tools.ShouldHaveSingleItem();
        fixture.Tools.OpenCount.ShouldBe(1);
        call.Answer.SetResult("Reply");
        var completed = await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        completed.Queue.ShouldBeEmpty();
        completed.ChatRevision.ShouldBeGreaterThan(first.ChatRevision);
        completed.HeadMessageId.ShouldNotBeNull();
    }

    [Fact]
    public async Task ClearShouldBehaveTheSameWhileAStoppedWorkerIsStillUnwinding()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cancellationObserved = fixture.Completion.DelayCancellation();
        var messageId = Guid.NewGuid();
        var pendingId = Guid.NewGuid();
        try
        {
            await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), messageId, "Question"));
            await fixture.NextCallAsync();
            await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), pendingId, "Waiting"));

            var stopped = await fixture.Dispatcher.StopAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId,
                CancellationToken.None);
            stopped!.Status.ShouldBe(ChatRunStatus.Paused);
            await cancellationObserved.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            // Clear used to remove the sent command too, but only while a worker happened to be
            // unwinding - the same click did different things depending on timing. It now removes
            // exactly what has not been sent, whenever it is called.
            var cleared = await fixture.Dispatcher.ClearAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId,
                CancellationToken.None);
            cleared!.Queue.ShouldHaveSingleItem().Id.ShouldBe(messageId);
            cleared.Queue[0].Stage.ShouldBe(QueuedMessageStage.UserCommitted);
        }
        finally
        {
            fixture.Completion.ReleaseCancellation();
        }

        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Paused);
        var emptied = await fixture.Dispatcher.ClearAllAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId,
            CancellationToken.None);
        emptied!.Queue.ShouldBeEmpty();
        emptied.Status.ShouldBe(ChatRunStatus.Idle);
    }

    [Fact]
    public async Task ForkShouldUseOnlyItsAncestorsAndKeepTheMainHead()
    {
        await using var fixture = await Fixture.CreateAsync();
        var message = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), message, "Original"));
        (await fixture.NextCallAsync()).Answer.SetResult("Original reply");
        var main = await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        var forkId = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), forkId, "Alternative", ChatSubmitMode.Fork, ParentMessageId: message));
        var fork = await fixture.NextCallAsync();
        fork.Request.ContextMessages!.Select(item => item.Content).ShouldBe(["Original", "Alternative"]);
        fork.Answer.SetResult("Alternative reply");
        await fixture.WaitAsync(run => run.BranchId == forkId && run.Status == ChatRunStatus.Completed);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Branches!.Single(branch => branch.Id == fixture.ChatId).HeadMessageId.ShouldBe(main.HeadMessageId);
    }

    [Fact]
    public async Task ConcurrentBranchesShouldNotShareTheirReplyParent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var root = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), root, "Root"));
        var main = await fixture.NextCallAsync();
        var forkId = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), forkId, "Fork", ChatSubmitMode.Fork, ParentMessageId: root));
        var fork = await fixture.NextCallAsync();
        fork.Answer.SetResult("Fork reply");
        await fixture.WaitAsync(run => run.BranchId == forkId && run.Status == ChatRunStatus.Completed);
        main.Answer.SetResult("Main reply");
        await fixture.WaitAsync(run => run.BranchId == fixture.ChatId && run.Status == ChatRunStatus.Completed);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Single(message => message.Content == "Main reply").ParentId.ShouldBe(root);
        chat.Messages.Single(message => message.Content == "Fork reply").ParentId.ShouldBe(forkId);
    }

    [Fact]
    public async Task ReplacingRootMessageMustPreserveOtherBranches()
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), original, "Original"));
        (await fixture.NextCallAsync()).Answer.SetResult("Original reply");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);

        var forkId = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), forkId, "Fork", ChatSubmitMode.Fork,
            BranchId: fixture.ChatId, ParentMode: MessageParentMode.Explicit, ParentMessageId: original));
        (await fixture.NextCallAsync()).Answer.SetResult("Fork reply");
        await fixture.WaitAsync(run => run.BranchId == forkId && run.Status == ChatRunStatus.Completed);

        var replacement = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), replacement, "Replacement",
            ChatSubmitMode.Replace, BranchId: fixture.ChatId, ReplaceSourceId: original));
        var call = await fixture.NextCallAsync();
        call.Request.ContextMessages!.Select(message => message.Content).ShouldBe(["Replacement"]);
        call.Answer.SetResult("Replacement reply");
        await fixture.WaitAsync(run => run.BranchId == fixture.ChatId && run.Status == ChatRunStatus.Completed);

        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        var branches = chat!.Branches!;
        ChatContext.Get(chat, branches.Single(branch => branch.Id == fixture.ChatId).HeadMessageId!.Value)
            .Select(message => message.Content).ShouldBe(["Replacement", "Replacement reply"]);
        ChatContext.Get(chat, branches.Single(branch => branch.Id == forkId).HeadMessageId!.Value)
            .Select(message => message.Content).ShouldBe(["Original", "Fork", "Fork reply"]);
    }

    [Fact]
    public async Task ReplacingMessageShouldRemoveAbandonedTailBeforeCompletion()
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), original, "Original"));
        (await fixture.NextCallAsync()).Answer.SetResult("Original reply");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);

        var replacement = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), replacement, "Replacement",
            ChatSubmitMode.Replace, BranchId: fixture.ChatId, ReplaceSourceId: original));
        var replacementCall = await fixture.NextCallAsync();

        var runningChat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        runningChat!.Messages.Select(message => message.Content).ShouldBe(["Replacement"]);

        replacementCall.Answer.SetResult("Replacement reply");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        var completedChat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        completedChat!.Messages.Select(message => message.Content).ShouldBe(["Replacement", "Replacement reply"]);
    }

    [Fact]
    public async Task WarmUpShouldRemoveReplacementTailsLeftByOlderBuilds()
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), original, "Original"));
        (await fixture.NextCallAsync()).Answer.SetResult("Original reply");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);

        await fixture.AppendLegacyReplacementAsync(original, Guid.NewGuid(), "Replacement");
        var staleChat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        staleChat!.Messages.Select(message => message.Content).ShouldBe(["Original", "Original reply", "Replacement"]);

        await fixture.RestartAsync();

        var cleanedChat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        cleanedChat!.Messages.Select(message => message.Content).ShouldBe(["Replacement"]);
    }

    [Fact]
    public async Task DeletingParentBranchMustKeepAndReparentChildBranch()
    {
        await using var fixture = await Fixture.CreateAsync();
        var root = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), root, "Root"));
        (await fixture.NextCallAsync()).Answer.SetResult("Root reply");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);

        var parentId = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), parentId, "Parent", ChatSubmitMode.Fork,
            BranchId: fixture.ChatId, ParentMode: MessageParentMode.Explicit, ParentMessageId: root));
        (await fixture.NextCallAsync()).Answer.SetResult("Parent reply");
        await fixture.WaitAsync(run => run.BranchId == parentId && run.Status == ChatRunStatus.Completed);

        var childId = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), childId, "Child", ChatSubmitMode.Fork,
            BranchId: parentId, ParentMode: MessageParentMode.Explicit, ParentMessageId: parentId));
        (await fixture.NextCallAsync()).Answer.SetResult("Child reply");
        await fixture.WaitAsync(run => run.BranchId == childId && run.Status == ChatRunStatus.Completed);

        var queuedId = Guid.NewGuid();
        var queued = await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), queuedId, "Queued child",
            ChatSubmitMode.Queue, BranchId: childId));
        queued.Status.ShouldBe(ChatRunStatus.Paused);

        var before = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        var result = await fixture.Dispatcher.DeleteBranchAsync(fixture.ProjectId, fixture.ChatId, parentId,
            before!.Revision, CancellationToken.None);

        result.IsDeleted.ShouldBeTrue();
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        var branches = chat!.Branches!;
        branches.ShouldNotContain(branch => branch.Id == parentId);
        var child = branches.Single(branch => branch.Id == childId);
        child.ParentBranchId.ShouldBe(fixture.ChatId);
        ChatContext.Get(chat, child.HeadMessageId!.Value).Select(message => message.Content)
            .ShouldBe(["Root", "Parent", "Child", "Child reply"]);

        var childRun = (await fixture.Dispatcher.GetSnapshotAsync(CancellationToken.None))
            .Single(run => run.BranchId == childId);
        childRun.Queue.ShouldHaveSingleItem().Id.ShouldBe(queuedId);
        await fixture.Dispatcher.ResumeAsync(fixture.ProjectId, fixture.ChatId, childId, CancellationToken.None);
        var resumed = await fixture.NextCallAsync();
        resumed.Request.ContextMessages!.Select(message => message.Content)
            .ShouldBe(["Root", "Parent", "Child", "Child reply", "Queued child"]);
        resumed.Answer.SetResult("Queued reply");
        await fixture.WaitAsync(run => run.BranchId == childId && run.Status == ChatRunStatus.Completed);
    }

    [Fact]
    public async Task StopAndResumeShouldReuseTheCommittedUserMessage()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Question"));
        await fixture.NextCallAsync();
        await fixture.Dispatcher.StopAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None);
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Paused);
        // Wait for the cancelled worker to finish before resuming.
        await fixture.Dispatcher.ShutdownAsync(CancellationToken.None);
        await fixture.RestartAsync();
        await fixture.Dispatcher.ResumeAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None);
        (await fixture.NextCallAsync()).Answer.SetResult("Reply");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Count(message => message.Role == "User").ShouldBe(1);
    }

    [Fact]
    public async Task FailedQueueCommitMustNotDeleteTheReplacedMessages()
    {
        await using var fixture = await Fixture.CreateAsync();
        var source = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), source, "Keep me"));
        (await fixture.NextCallAsync()).Answer.SetResult("Keep reply");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        fixture.FileSystem.FailWriteSuffix = ".run.json.tmp";
        await Should.ThrowAsync<IOException>(() => fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Replacement",
            ChatSubmitMode.Replace, ReplaceSourceId: source)));
        fixture.FileSystem.FailWriteSuffix = null;
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Select(message => message.Content).ShouldBe(["Keep me", "Keep reply"]);
    }

    [Fact]
    public async Task ImmediateResumeAfterStopShouldFinishWithoutDuplicatingTheUserMessage()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Question"));
        await fixture.NextCallAsync();
        await fixture.Dispatcher.StopAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None);
        await fixture.Dispatcher.ResumeAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None);
        (await fixture.NextCallAsync()).Answer.SetResult("Reply");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Select(message => message.Content).ShouldBe(["Question", "Reply"]);
    }

    [Fact]
    public async Task DeletingAnActiveChatShouldCancelItsWorkerAndRemovePersistedRuns()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Question"));
        var call = await fixture.NextCallAsync();
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        var result = await fixture.Dispatcher.DeleteChatAsync(fixture.ProjectId, fixture.ChatId, chat!.Revision, CancellationToken.None);
        result.IsDeleted.ShouldBeTrue();
        call.Answer.TrySetResult("Too late");
        (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None)).ShouldBeNull();
        await fixture.RestartAsync();
        (await fixture.Dispatcher.GetSnapshotAsync(CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task StoppingShouldKeepWhatTheModelHadAlreadyWritten()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StreamPreludeAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Question"), "Half an");

        await fixture.Dispatcher.StopAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None, Guid.NewGuid());
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Paused);

        // The user message keeps its answer instead of being left alone in the transcript, and
        // the answer says for itself that it is unfinished.
        var truncated = await fixture.WaitForMessageAsync(message => message.Role == "Assistant");
        truncated.Content.ShouldBe("Half an");
        truncated.IsIncomplete.ShouldBeTrue();

        // The command is still queued, because Resume is expected to rebuild it - but it is no
        // longer the active one, so nothing is left claiming to be running.
        var paused = await fixture.WaitAsync(run => run.Status == ChatRunStatus.Paused && run.ActiveMessageId is null);
        paused.Queue.ShouldHaveSingleItem().Stage.ShouldBe(QueuedMessageStage.UserCommitted);
    }

    [Fact]
    public async Task ResumingShouldReplaceTheTruncatedAnswerRatherThanAddToIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StreamPreludeAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Question"), "Half an");
        await fixture.Dispatcher.StopAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None, Guid.NewGuid());
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Paused);
        await fixture.WaitForMessageAsync(message => message.Role == "Assistant");

        await fixture.Dispatcher.ResumeAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None, Guid.NewGuid());
        (await fixture.NextCallAsync()).Answer.SetResult("A whole answer");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);

        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        var answers = chat!.Messages.Where(message => message.Role == "Assistant").ToArray();
        answers.ShouldHaveSingleItem().Content.ShouldBe("A whole answer");
        answers[0].IsIncomplete.ShouldBeFalse();
    }

    [Fact]
    public async Task SendNowShouldInterruptAndAnswerTheNewMessageFirst()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StreamPreludeAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Slow question"), "Thinking");
        var queuedId = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), queuedId, "Later question"));

        var urgentId = Guid.NewGuid();
        var snapshot = await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), urgentId, "Answer this now", ChatSubmitMode.SendNow));

        // The interrupted command is gone rather than kept for a retry, the urgent message is
        // first, and what was queued behind it keeps its place.
        snapshot.Queue.Select(item => item.Id).ShouldBe([urgentId, queuedId]);
        var urgent = await fixture.NextCallAsync();
        urgent.Request.Message.ShouldBe("Answer this now");
        urgent.Answer.SetResult("Right away");
        await fixture.WaitAsync(run => run.Queue.Count == 1);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.ShouldContain(message => message.Content == "Thinking" && message.IsIncomplete);
    }

    [Fact]
    public async Task SendingQueuedMessageNowShouldInterruptAndPromoteTheSelectedMessage()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StreamPreludeAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Slow question"), "Thinking");
        var firstQueuedId = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), firstQueuedId, "First queued"));
        var selectedId = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), selectedId, "Send this now"));

        var snapshot = await fixture.Dispatcher.SendQueuedNowAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId,
            selectedId, CancellationToken.None, Guid.NewGuid());

        snapshot!.Queue.Select(item => item.Id).ShouldBe([selectedId, firstQueuedId]);
        var selected = await fixture.NextCallAsync();
        selected.Request.Message.ShouldBe("Send this now");
        selected.Answer.SetResult("Sent first");
        await fixture.WaitAsync(run => run.Queue.Count == 1);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.ShouldContain(message => message.Content == "Thinking" && message.IsIncomplete);
    }

    [Fact]
    public async Task SendNowFromComposerShouldKeepCompletedToolBatchesValid()
    {
        await using var fixture = await Fixture.CreateAsync();
        await StartRunAfterTwoToolBatchesAsync(fixture);

        var urgent = await fixture.SubmitAsync(new SubmitChatMessageRequest(
            Guid.NewGuid(), Guid.NewGuid(), "Composer urgent", ChatSubmitMode.SendNow));

        urgent.Queue.ShouldHaveSingleItem().Content.ShouldBe("Composer urgent");
        var call = await fixture.NextCallAsync();
        AssertValidToolContext(call.Request.ContextMessages!, "Composer urgent");
        call.Answer.SetResult("Done");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
    }

    [Fact]
    public async Task SendNowFromQueueShouldKeepCompletedToolBatchesValid()
    {
        await using var fixture = await Fixture.CreateAsync();
        await StartRunAfterTwoToolBatchesAsync(fixture);
        var ordinaryId = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), ordinaryId, "Ordinary queued"));
        var urgentId = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), urgentId, "Queued urgent"));

        var urgent = await fixture.Dispatcher.SendQueuedNowAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId,
            urgentId, CancellationToken.None, Guid.NewGuid());

        urgent!.Queue.Select(item => item.Id).ShouldBe([urgentId, ordinaryId]);
        var call = await fixture.NextCallAsync();
        AssertValidToolContext(call.Request.ContextMessages!, "Queued urgent");
        call.Answer.SetResult("Done");
        await fixture.WaitAsync(run => run.Queue.Count == 1);
    }

    [Fact]
    public async Task RemovingAQueuedMessageClaimedAfterToolBatchesShouldInterruptOnlyThatMessage()
    {
        await using var fixture = await Fixture.CreateAsync();
        var claimedId = await StartRunAfterTwoToolBatchesAsync(fixture);

        var removed = await fixture.Dispatcher.RemoveQueuedAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId,
            claimedId, CancellationToken.None, Guid.NewGuid());

        removed!.Queue.ShouldBeEmpty();
        removed.ActiveMessageId.ShouldBeNull();

        var nextId = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), nextId, "Continue after removal"));
        var next = await fixture.NextCallAsync();
        AssertValidToolContext(next.Request.ContextMessages!, "Continue after removal");
        next.Answer.SetResult("Done");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
    }

    private static async Task<Guid> StartRunAfterTwoToolBatchesAsync(Fixture fixture)
    {
        await fixture.SetPolicyAsync("Allow");
        var messageId = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), messageId, "Use several tools"));
        var first = await fixture.NextCallAsync();
        first.ToolCalls =
        [
            new ChatToolCall("batch-1-call-1", "mcp_built_in__process_run", "{}"),
            new ChatToolCall("batch-1-call-2", "mcp_built_in__process_run", "{}")
        ];
        first.Answer.SetResult("");
        var second = await fixture.NextCallAsync();
        second.ToolCalls =
        [
            new ChatToolCall("batch-2-call-1", "mcp_built_in__process_run", "{}"),
            new ChatToolCall("batch-2-call-2", "mcp_built_in__process_run", "{}")
        ];
        second.Answer.SetResult("");
        await fixture.NextCallAsync();
        fixture.Tools.CallCount.ShouldBe(4);
        return messageId;
    }

    private static void AssertValidToolContext(IReadOnlyList<ChatCompletionMessage> context, string lastUserMessage)
    {
        var expectedResults = new Queue<string>();
        foreach (var message in context)
        {
            if (expectedResults.Count > 0)
            {
                message.Role.ShouldBe("tool");
                message.ToolCallId.ShouldBe(expectedResults.Dequeue());
            }
            else
            {
                message.Role.ShouldNotBe("tool");
            }

            foreach (var call in message.ToolCalls ?? []) expectedResults.Enqueue(call.Id);
        }

        expectedResults.ShouldBeEmpty();
        context[^1].Role.ShouldBe("user");
        context[^1].Content.ShouldBe(lastUserMessage);
    }

    [Fact]
    public async Task ClearingShouldRemoveWhatIsWaitingAndLeaveTheRunningMessageAlone()
    {
        await using var fixture = await Fixture.CreateAsync();
        var runningId = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), runningId, "Running"));
        var call = await fixture.NextCallAsync();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Waiting one", ChatSubmitMode.Queue));
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Paused);

        var cleared = await fixture.Dispatcher.ClearAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None, Guid.NewGuid());

        cleared!.Queue.ShouldHaveSingleItem().Id.ShouldBe(runningId);
        cleared.Queue[0].Stage.ShouldBe(QueuedMessageStage.UserCommitted);
        call.Answer.SetResult("Done");
    }

    [Fact]
    public async Task ClearingEverythingShouldStopTheRunAndEmptyTheQueue()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StreamPreludeAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Running"), "Partial");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Waiting"));

        var cleared = await fixture.Dispatcher.ClearAllAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None, Guid.NewGuid());

        cleared!.Queue.ShouldBeEmpty();
        cleared.Status.ShouldBe(ChatRunStatus.Idle);
    }

    [Fact]
    public async Task DiscardingShouldDropTheStoppedCommandAndLetTheQueueContinue()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StreamPreludeAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Stuck"), "Partial");
        var nextId = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), nextId, "Next"));

        var discarded = await fixture.Dispatcher.DiscardAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None, Guid.NewGuid());

        discarded!.Queue.ShouldHaveSingleItem().Id.ShouldBe(nextId);
        var next = await fixture.NextCallAsync();
        next.Request.Message.ShouldBe("Next");
        next.Answer.SetResult("Answered");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
    }

    [Fact]
    public async Task DeletingAChatMidGenerationShouldNotBeDefeatedByItsOwnShutdown()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StreamPreludeAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Question"), "Half an");
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);

        var result = await fixture.Dispatcher.DeleteChatAsync(fixture.ProjectId, fixture.ChatId, chat!.Revision, CancellationToken.None);

        // Stopping the run would otherwise commit its truncated answer and move the revision the
        // delete was checked against, turning the caller's own cleanup into a conflict.
        result.IsDeleted.ShouldBeTrue();
        (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task InterruptedRunShouldKeepItsPartialAnswerAcrossRestart()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StreamPreludeAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Question"), "Half an");
        await fixture.Dispatcher.StopAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None, Guid.NewGuid());
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Paused);
        await fixture.WaitForMessageAsync(message => message.Role == "Assistant");
        await fixture.RestartAsync();

        // A partial answer must never satisfy the "this command already has its reply" check, or
        // resuming after a restart would quietly close the command without generating anything.
        await fixture.Dispatcher.ResumeAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None, Guid.NewGuid());
        (await fixture.NextCallAsync()).Answer.SetResult("A whole answer");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);

        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Where(message => message.Role == "Assistant")
            .ShouldHaveSingleItem().Content.ShouldBe("A whole answer");
    }

    [Fact]
    public async Task AnswersCutOffAtTheTokenLimitShouldBeContinuedIntoOneMessage()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Write at length"));

        var cut = await fixture.NextCallAsync();
        cut.FinishReason = "length";
        cut.Answer.SetResult("The first half");
        var rest = await fixture.NextCallAsync();
        rest.Answer.SetResult(" and the second half.");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);

        // One answer, not two, and no seam where the ceiling fell.
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Where(message => message.Role == "Assistant")
            .ShouldHaveSingleItem().Content.ShouldBe("The first half and the second half.");

        // The model was told to carry on, and the person was not: the instruction exists only in
        // the context the agent assembles, never in the chat it stores.
        rest.Request.ContextMessages![^1].Content.ShouldContain("cut off at the output token limit");
        chat.Messages.ShouldAllBe(message => !message.Content.Contains("cut off at the output token limit"));
    }

    [Fact]
    public async Task AnswersThatEndNormallyShouldNotBeContinued()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Answer briefly"));

        var only = await fixture.NextCallAsync();
        only.FinishReason = "stop";
        only.Answer.SetResult("Short.");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);

        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Where(message => message.Role == "Assistant")
            .ShouldHaveSingleItem().Content.ShouldBe("Short.");
    }

    private sealed record Call(ChatCompletionRequest Request, TaskCompletionSource<string> Answer)
    {
        public IReadOnlyList<ChatToolCall>? ToolCalls { get; set; }

        /// <summary>Text streamed before the call is answered, for tests that interrupt mid-answer.</summary>
        public string? Prelude { get; set; }

        /// <summary>Completes once <see cref="Prelude"/> has been yielded to the agent.</summary>
        public TaskCompletionSource<bool> PreludeStreamed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>What the stream reports as its reason for stopping; "length" for a cut-off answer.</summary>
        public string? FinishReason { get; set; }
    }
    private sealed class Completion : IChatCompletionClient
    {
        private TaskCompletionSource<bool>? _cancellationObserved;
        private TaskCompletionSource<bool>? _cancellationRelease;
        public Channel<Call> Calls { get; } = Channel.CreateUnbounded<Call>();

        /// <summary>Text the next call streams before it is answered; consumed once.</summary>
        public string? NextPrelude { get; set; }
        public Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(ChatCompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var call = new Call(request, new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));
            Calls.Writer.TryWrite(call);
            call.Prelude = NextPrelude;
            NextPrelude = null;
            if (call.Prelude is { Length: > 0 } prelude)
            {
                yield return new ChatCompletionChunk(prelude);
                call.PreludeStreamed.TrySetResult(true);
            }
            string content;
            try
            {
                content = await call.Answer.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (_cancellationRelease is { } release)
            {
                _cancellationObserved!.TrySetResult(true);
                await release.Task;
                throw;
            }
            yield return new ChatCompletionChunk(content, ToolCalls: call.ToolCalls, FinishReason: call.FinishReason);
        }

        public Task<bool> DelayCancellation()
        {
            _cancellationObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _cancellationRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _cancellationObserved.Task;
        }

        public void ReleaseCancellation() => _cancellationRelease?.TrySetResult(true);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public MemoryFileSystem FileSystem { get; } = new();
        public Completion Completion { get; } = new();
        public TestTools Tools { get; } = new();
        private readonly ChatSynchronization _synchronization = new();
        private readonly SystemClock _clock = new();
        private readonly Uuid7IdGenerator _ids = new();
        private readonly IGlobalSecretStore _secrets = Mock.Of<IGlobalSecretStore>();
        private readonly JsonChatRepository _chatRepository;
        private readonly JsonProjectRepository _projects;
        private readonly JsonChatRunRepository _runs;
        private readonly JsonGlobalSettingsRepository _settings;
        private readonly ProjectService _projectService;
        public ChatService Chats { get; }
        public ChatRunDispatcher Dispatcher { get; private set; }
        public Guid ProjectId { get; private set; }
        public Guid ChatId { get; private set; }
        private Fixture(IWorkspaceChangeTracker? workspace = null)
        {
            _chatRepository = new JsonChatRepository(FileSystem, new ChatStoragePaths("data"));
            _projects = new JsonProjectRepository(FileSystem, new ProjectStoragePaths("data"));
            _runs = new JsonChatRunRepository(FileSystem, new ChatRunStoragePaths("data"));
            _settings = new JsonGlobalSettingsRepository(FileSystem, new GlobalSettingsPaths("data"));
            _projectService = new ProjectService(_projects, _ids, _clock, _settings);
            Chats = new ChatService(_chatRepository, _ids, _clock, _synchronization);
            Workspace = workspace ?? new WorkspaceChangeTracker();
            Dispatcher = NewDispatcher();
        }
        public IWorkspaceChangeTracker Workspace { get; }
        private ChatRunDispatcher NewDispatcher()
        {
            var policies = new ToolPolicyResolver(_projectService, Chats, _settings);
            return new ChatRunDispatcher(_runs, Chats, Chats, _projectService, _settings,
                new GlobalSettingsService(_settings, _secrets),
                new ChatAgent(Completion, () => Tools, _projectService, _settings, policies, Workspace),
                _secrets, _clock, _ids, _synchronization, Workspace, policies);
        }
        public static async Task<Fixture> CreateAsync(IWorkspaceChangeTracker? workspace = null)
        {
            var fixture = new Fixture(workspace);
            await fixture._settings.SaveAsync(new GlobalSettings([new ConnectionSettings(Guid.NewGuid(), "Test", "https://example.test/v1", "model", true, true, false)], [], []), CancellationToken.None);
            fixture.ProjectId = (await fixture._projectService.CreateAsync(new CreateProjectRequest("Test", ""), CancellationToken.None)).Id;
            fixture.ChatId = (await fixture.Chats.CreateAsync(fixture.ProjectId, new CreateChatRequest("Chat"), CancellationToken.None)).Id;
            await fixture.Dispatcher.WarmUpAsync(CancellationToken.None);
            return fixture;
        }
        public Task<ChatRunSnapshot> SubmitAsync(SubmitChatMessageRequest request) => Dispatcher.SubmitAsync(ProjectId, ChatId, request, CancellationToken.None);
        public async Task AppendLegacyReplacementAsync(Guid sourceId, Guid replacementId, string content)
        {
            var stored = await _chatRepository.GetAsync(new Domain.Projects.ProjectId(ProjectId),
                new Domain.Chats.ChatId(ChatId), CancellationToken.None) ?? throw new InvalidOperationException("Chat not found.");
            var source = stored.Chat.Messages.Single(message => message.Id.Value == sourceId);
            stored.Chat.ReplaceInBranch(ChatId, source.Id,
                new Domain.Chats.ChatMessage(new Domain.Chats.ChatMessageId(replacementId), source.ParentId,
                    Domain.Chats.ChatMessageRole.User, content, _clock.UtcNow), _clock.UtcNow);
            var result = await _chatRepository.SaveAsync(stored.Chat, stored.Revision, CancellationToken.None);
            if (!result.IsSaved) throw new InvalidOperationException("Legacy replacement was not saved.");
        }
        /// <summary>Waits for the agent to finish writing tool answers, which outlives the status change.</summary>
        public async Task<IReadOnlyList<string?>> WaitForToolAnswersAsync(int expected)
        {
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
            while (true)
            {
                var chat = await Chats.GetAsync(ProjectId, ChatId, CancellationToken.None);
                var answers = chat!.Messages.Where(message => message.ToolCallId is not null)
                    .Select(message => message.ToolCallId).ToArray();
                if (answers.Length >= expected || DateTimeOffset.UtcNow > deadline) return answers;
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
        }

        public Task<ProjectDetails?> GetProjectAsync() => _projectService.GetAsync(ProjectId, CancellationToken.None);
        public Task<GlobalSettings> GetGlobalAsync() => _settings.LoadAsync(CancellationToken.None);
        public async Task SetGlobalPolicyAsync(string decision)
        {
            var global = await _settings.LoadAsync(CancellationToken.None);
            await _settings.SaveAsync(global with
            {
                McpServers = [DefaultMcpServer.Settings with { Policy = "Allow" }],
                ToolPolicies = [new McpToolPolicySettings(DefaultMcpServer.Id, "process_run", "schema", decision, 20, 120)]
            }, CancellationToken.None);
        }

        public async Task SetPolicyAsync(string decision, long timeoutSeconds = 120)
        {
            var global = await _settings.LoadAsync(CancellationToken.None);
            await _settings.SaveAsync(global with { McpServers = [DefaultMcpServer.Settings with { Policy = "Allow" }] }, CancellationToken.None);
            var project = await _projectService.GetAsync(ProjectId, CancellationToken.None);
            await _projectService.UpdateSecurityAsync(ProjectId, new UpdateProjectSecurityRequest(project!.Revision, [],
                [new Contracts.Projects.McpServerSettings(DefaultMcpServer.Id, "Default", "Stdio", true)],
                [new ToolPolicySettings(DefaultMcpServer.Id, "process_run", "schema", decision, 20, timeoutSeconds)]), CancellationToken.None);
        }
        public async Task<Call> NextCallAsync() => await Completion.Calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        /// <summary>
        /// Submits a message, lets its call stream <paramref name="text"/>, and leaves the call
        /// unanswered - the state an interruption has to cope with. Arming the prelude and
        /// submitting are one step because the worker can reach the endpoint immediately.
        /// </summary>
        public async Task<Call> StreamPreludeAsync(SubmitChatMessageRequest request, string text)
        {
            Completion.NextPrelude = text;
            await SubmitAsync(request);
            var call = await NextCallAsync();
            await call.PreludeStreamed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitAsync(run => run.StreamingContent.Contains(text, StringComparison.Ordinal));
            return call;
        }

        public async Task<ChatMessageView> WaitForMessageAsync(Func<ChatMessageView, bool> predicate)
        {
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
            while (true)
            {
                var chat = await Chats.GetAsync(ProjectId, ChatId, CancellationToken.None);
                if (chat!.Messages.FirstOrDefault(predicate) is { } message) return message;
                if (DateTimeOffset.UtcNow > deadline) throw new InvalidOperationException("No matching message.");
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
        }
        public async Task<ChatRunSnapshot> WaitAsync(Func<ChatRunSnapshot, bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await foreach (var snapshots in Dispatcher.SubscribeAsync(timeout.Token))
                if (snapshots.FirstOrDefault(predicate) is { } snapshot) return snapshot;
            throw new InvalidOperationException("No matching snapshot.");
        }
        public async Task RestartAsync()
        {
            await Dispatcher.DisposeAsync();
            Dispatcher = NewDispatcher();
            await Dispatcher.WarmUpAsync(CancellationToken.None);
        }
        public async ValueTask DisposeAsync()
        {
            await Dispatcher.DisposeAsync();
            _chatRepository.Dispose();
            _projects.Dispose();
            _runs.Dispose();
        }
    }

    private sealed class TestWorkspaceChangeTracker : IWorkspaceChangeTracker
    {
        private readonly Queue<WorkspaceChangeSet> _queued = new();
        private WorkspaceChangeSet _current = WorkspaceChangeSet.Empty;

        public void Enqueue(WorkspaceChangeSet changes) => _queued.Enqueue(changes);

        public Task BeginRunAsync(WorkspaceRunKey run, IReadOnlyList<ToolDirectoryGrant> grants, CancellationToken cancellationToken)
        {
            _current = _queued.TryDequeue(out var changes) ? changes : WorkspaceChangeSet.Empty;
            return Task.CompletedTask;
        }

        public Task RecordIntentAsync(WorkspaceRunKey run, ToolDescriptor tool, string arguments, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RecordEffectAsync(WorkspaceRunKey run, ToolDescriptor tool, string arguments, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<WorkspaceChangeSet> SnapshotAsync(WorkspaceRunKey run, CancellationToken cancellationToken) =>
            Task.FromResult(_current);

        public Task CompleteRunAsync(WorkspaceRunKey run, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestTools : IToolSessionFactory, IToolSession
    {
        public int CallCount { get; private set; }
        public int OpenCount { get; private set; }
        public IReadOnlyList<AgentTool> Tools { get; } = [new(
            new ChatToolDefinition("mcp_built_in__process_run", "Run", JsonSerializer.Deserialize<JsonElement>("{}")),
            ToolDescriptor.Basic("mcp_built_in__process_run", "process_run", "Run", JsonSerializer.Deserialize<JsonElement>("{}")),
            DefaultMcpServer.Id, "process_run", "schema")];
        public IReadOnlyList<ToolDirectoryGrant> Grants { get; private set; } = [];
        public IReadOnlySet<Guid> Servers { get; private set; } = new HashSet<Guid>();
        public Task<IToolSession> OpenAsync(IReadOnlyList<ToolDirectoryGrant> directoryGrants, IReadOnlySet<Guid> servers,
            CancellationToken cancellationToken)
        {
            OpenCount++;
            Grants = directoryGrants;
            Servers = servers;
            return Task.FromResult<IToolSession>(this);
        }
        public string ValidateArguments(AgentTool tool, string arguments) => arguments;
        /// <summary>Set to make the next call hang until its own policy timeout cancels it.</summary>
        public bool HangNextCall { get; set; }

        /// <summary>Set to make the next call work — and say so — for this long before answering.</summary>
        public TimeSpan ReportNextCallFor { get; set; }

        public async Task<ToolCallResult> CallAsync(AgentTool tool, string arguments, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
        {
            CallCount++;
            if (HangNextCall)
            {
                HangNextCall = false;
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (ReportNextCallFor > TimeSpan.Zero)
            {
                var until = Environment.TickCount64 + (long)ReportNextCallFor.TotalMilliseconds;
                ReportNextCallFor = TimeSpan.Zero;
                while (Environment.TickCount64 < until)
                {
                    await Task.Delay(100, cancellationToken);
                    progress?.Report(new ToolProgress(1, 4, "still working"));
                }
            }

            return ToolResultCodec.Read("{\"structuredContent\":{\"exitCode\":0}}");
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
