namespace AI.Infrastructure.Tests.Storage;

using AI.Contracts.Chats;
using AI.Contracts.Runs;
using AI.Contracts.Settings;
using AI.Contracts.Tools;
using Shouldly;
using Xunit;

public sealed partial class ChatExecutionTests
{
    [Fact]
    public async Task RepeatedBoundedSubtaskWaitsDoNotStallAnOtherwiseHealthyRun()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetChatAutomationAsync(new ChatAutomationSettings(AutoTitle: false, SuggestReplies: false));
        await fixture.Chats.UpdateApprovalModeAsync(fixture.ProjectId, fixture.ChatId,
            new UpdateChatApprovalModeRequest(ToolApprovalMode.FullAccess), CancellationToken.None);
        fixture.Tools.OfferSubtasks = true;
        await fixture.SubmitAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Implement policies"));
        var launch = await fixture.NextCallAsync();
        launch.ToolCalls = [new("launch", "mcp_app__spawn_subtask", "{\"task\":\"policy\"}")];
        launch.Answer.SetResult("");
        await fixture.Tools.SubtaskStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        for (var index = 0; index < 6; index++)
        {
            var waiting = await fixture.NextCallAsync();
            waiting.ToolCalls = [new($"wait-{index}", "mcp_app__subtask_wait",
                $"{{\"taskIds\":[\"{fixture.Tools.SubtaskId}\"],\"waitTimeoutMs\":10}}")];
            waiting.Answer.SetResult("");
        }
        var finishing = await fixture.NextCallAsync();
        fixture.Tools.SubtaskRelease.SetResult();
        finishing.Answer.SetResult("Finish after collection");
        (await fixture.NextCallAsync()).Answer.SetResult("Integrated completion");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        fixture.Tools.SubtaskCancelled.Task.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task ParentWorksAfterLaunchAndCollectsUnreadResultsBeforeItsFinalAnswer()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetChatAutomationAsync(new ChatAutomationSettings(AutoTitle: false, SuggestReplies: false));
        await fixture.Chats.UpdateApprovalModeAsync(fixture.ProjectId, fixture.ChatId,
            new UpdateChatApprovalModeRequest(ToolApprovalMode.FullAccess), CancellationToken.None);
        fixture.Tools.OfferSubtasks = true;
        await fixture.SubmitAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Implement policies"));
        var launch = await fixture.NextCallAsync();
        launch.ToolCalls = [new("launch", "mcp_app__spawn_subtask", "{\"task\":\"policy\"}")];
        launch.Answer.SetResult("");
        var own = await fixture.NextCallAsync();
        own.ToolCalls = [new("own", "mcp_built_in__process_run_sandboxed", "{}")];
        own.Answer.SetResult("");
        var premature = await fixture.NextCallAsync();
        fixture.Tools.SubtaskRelease.Task.IsCompleted.ShouldBeFalse();
        premature.Answer.SetResult("Premature completion");
        await fixture.Tools.SubtaskWaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        fixture.Tools.SubtaskRelease.SetResult();
        var integrated = await fixture.NextCallAsync();
        integrated.Request.ContextMessages!.ShouldContain(message => message.ToolCallId != null
            && message.ForModel.Contains("Verified policy", StringComparison.Ordinal));
        integrated.Answer.SetResult("Integrated completion");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        var chat = (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None))!;
        chat.Messages[^1].Content.ShouldBe("Integrated completion");
        chat.Messages.ShouldNotContain(message => message.Content == "Premature completion");
        fixture.Tools.SubtaskCancelled.Task.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task JoinedMessageInterruptsSubtaskWaitWithoutCancellingItsWorker()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetChatAutomationAsync(new ChatAutomationSettings(AutoTitle: false, SuggestReplies: false));
        await fixture.Chats.UpdateApprovalModeAsync(fixture.ProjectId, fixture.ChatId,
            new UpdateChatApprovalModeRequest(ToolApprovalMode.FullAccess), CancellationToken.None);
        fixture.Tools.OfferSubtasks = true;
        await fixture.SubmitAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Implement policies"));
        var launch = await fixture.NextCallAsync();
        launch.ToolCalls = [new("launch", "mcp_app__spawn_subtask", "{\"task\":\"policy\"}")];
        launch.Answer.SetResult("");
        var waiting = await fixture.NextCallAsync();
        var id = fixture.Tools.SubtaskId!;
        waiting.ToolCalls = [new("wait", "mcp_app__subtask_wait", $"{{\"taskIds\":[\"{id}\"]}}")];
        waiting.Answer.SetResult("");
        await fixture.Tools.SubtaskWaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var hint = Guid.NewGuid();
        await fixture.SubmitAsync(new(Guid.NewGuid(), hint, "Keep the policy; use the new receipt contract"));
        var resumed = await fixture.NextCallAsync();
        resumed.Request.ContextMessages!.ShouldContain(message => message.MessageId == hint);
        fixture.Tools.SubtaskCancelled.Task.IsCompleted.ShouldBeFalse();
        fixture.Tools.SubtaskRelease.SetResult();
        resumed.ToolCalls = [new("collect", "mcp_app__subtask_wait", $"{{\"taskIds\":[\"{id}\"]}}")];
        resumed.Answer.SetResult("");
        (await fixture.NextCallAsync()).Answer.SetResult("Integrated with the new contract");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed && run.Queue.Count == 0);
        fixture.Tools.SubtaskCancelled.Task.IsCompleted.ShouldBeFalse();
        var chat = (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None))!;
        chat.Messages.Single(message => message.ToolCallId == "wait").Content.ShouldContain("ended early");
    }

    [Fact]
    public async Task StopOfTheParentCancelsItsBackgroundWorker()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetChatAutomationAsync(new ChatAutomationSettings(AutoTitle: false, SuggestReplies: false));
        await fixture.Chats.UpdateApprovalModeAsync(fixture.ProjectId, fixture.ChatId,
            new UpdateChatApprovalModeRequest(ToolApprovalMode.FullAccess), CancellationToken.None);
        fixture.Tools.OfferSubtasks = true;
        await fixture.SubmitAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Implement policies"));
        var launch = await fixture.NextCallAsync();
        launch.ToolCalls = [new("launch", "mcp_app__spawn_subtask", "{\"task\":\"policy\"}")];
        launch.Answer.SetResult("");
        await fixture.NextCallAsync();
        await fixture.Tools.SubtaskStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await fixture.Dispatcher.StopAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None);
        await fixture.Tools.SubtaskCancelled.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Paused);
    }
}
