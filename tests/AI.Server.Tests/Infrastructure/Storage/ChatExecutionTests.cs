namespace AI.Infrastructure.Tests.Storage;

using AI.Application.Chat;
using Application.Chats;
using AI.Application.Projects;
using Application.Runs;
using AI.Application.Settings;
using AI.Contracts.Chat;
using Contracts.Chats;
using AI.Contracts.Projects;
using Contracts.Runs;
using AI.Contracts.Settings;
using Projects;
using Settings;
using AI.Infrastructure.Storage;
using AI.Infrastructure.Chat;
using Moq;
using Shouldly;
using Xunit;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AI.Application.Tools;
using AI.Application.Workspace;
using AI.Contracts.Tools;
using AI.Contracts.Workspace;
using AI.Infrastructure.Workspace;
using AI.Server.Hosting;
using System.Text.Json;

public sealed class ChatExecutionTests
{

    [Fact]
    public async Task DirectoryGrantAddedDuringRunReopensToolSessionBeforeNextModelStep()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetPolicyAsync("Allow");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Read another project"));

        var first = await fixture.NextCallAsync();
        fixture.Tools.OpenCount.ShouldBe(1);
        await fixture.AddGrantAsync(@"C:\Projects\DevTeam\dotnet-matrix");
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        first.Answer.SetResult("");

        var next = await fixture.NextCallAsync();
        fixture.Tools.OpenCount.ShouldBe(2);
        fixture.Tools.Grants.ShouldHaveSingleItem().Root.ShouldBe(@"C:\Projects\DevTeam\dotnet-matrix");
        next.ToolCalls = [new ChatToolCall("call-2", "mcp_built_in__process_run", "{}")];
        next.Answer.SetResult("");
        (await fixture.NextCallAsync()).Answer.SetResult("Read complete");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        fixture.Tools.CallCount.ShouldBe(2);
    }

    [Fact]
    public async Task AnswerShouldStartADraftOfTheReplyWhenSuggestionsAreOn()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetChatAutomationAsync(new ChatAutomationSettings(SuggestReplies: true));
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Fix the empty list case"));
        (await fixture.NextCallAsync()).Answer.SetResult("Fixed it. Shall I add tests?");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);

        var draft = await fixture.NextCallAsync();
        draft.Request.Tools.ShouldBeNull();
        draft.Request.ContextMessages![^1].Content.ShouldContain("Shall I add tests?");
        draft.Answer.SetResult("Yes, add them and run them.");
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        var head = chat!.Branches!.Single(branch => branch.Id == fixture.ChatId).HeadMessageId!.Value;
        var suggestion = await fixture.ReplySuggestions.GetAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, head,
            false, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        suggestion.ShouldNotBeNull().Text.ShouldBe("Yes, add them and run them.");
    }

    [Fact]
    public async Task RoutedPlaybookShouldBeLoadedBeforeTheModelsFirstStep()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Tools.OfferRunSkill = true;
        fixture.Completion.RouteAnswer = "{\"skills\":[\"chat-summary\"],\"tools\":[]}";
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "What did we decide?"));

        var first = await fixture.NextCallAsync();
        // The model's first request already holds the loaded playbook and says to follow it.
        var context = first.Request.ContextMessages!;
        context.ShouldContain(message => message.Role == "assistant"
            && message.ToolCalls!.Single().Name == "mcp_app__run_skill");
        context[^1].Role.ShouldBe("tool");
        context[^1].ForModel.ShouldContain("Summarize the chat");
        context.ShouldContain(message => message.Role == "system" && message.Content.StartsWith(
            "Skill routing: the application loaded the skill chat-summary", StringComparison.Ordinal));
        fixture.Tools.SkillRuns.ShouldHaveSingleItem().ShouldContain("\"skillId\":\"chat-summary\"");
        first.Answer.SetResult("We decided to ship on Friday.");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);

        // The load is part of the transcript, so the skill shows and a later turn sees it.
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.ShouldContain(message => message.Role == "Tool");
        chat.Messages[^1].Content.ShouldBe("We decided to ship on Friday.");
    }

    [Fact]
    public async Task ToolMessagesShouldBePublishedAsAContiguousChatDelta()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetPolicyAsync("Allow");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Run command"));

        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        first.Answer.SetResult("");

        var published = await fixture.WaitAsync(run => run.MessageDelta?.Appends.Count >= 2);
        var appends = published.MessageDelta!.Appends;
        appends[^2].Revision.ShouldBe(appends[^1].BaseRevision);
        appends[^2].Message.ToolCalls.ShouldHaveSingleItem().Name.ShouldBe("mcp_built_in__process_run");
        appends[^1].Message.ToolCallId.ShouldBe("call-1");
        published.ChatRevision.ShouldBe(appends[^1].Revision);

        (await fixture.NextCallAsync()).Answer.SetResult("Done");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
    }

    [Fact]
    public async Task RunShouldPublishHowTheContextWindowIsFilledIncludingTheAnswer()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Hello"));

        var call = await fixture.NextCallAsync();
        var measured = await fixture.WaitAsync(run => run.Context is not null);
        var request = measured.Context!;
        request.ContextWindowTokens.ShouldBeGreaterThan(0);
        request.InstructionTokens.ShouldBeGreaterThan(0);
        request.HistoryTokens.ShouldBeGreaterThan(0);
        request.ReservedOutputTokens.ShouldBeGreaterThan(0);
        request.OverheadTokens.ShouldBeGreaterThan(0);

        call.Answer.SetResult(new string('a', 2_000));
        var completed = await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        completed.Context.ShouldNotBeNull().HistoryTokens.ShouldBeGreaterThanOrEqualTo(request.HistoryTokens + 1_000);
    }

    [Fact]
    public async Task StepInFlightShouldBeLiveAndHandOverToItsPreambleInOnePublication()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetPolicyAsync("Allow");
        var call = await fixture.StreamPreludeAsync(
            new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Run command"), "Checking the setup");

        var drafting = await fixture.WaitAsync(run => run.DraftContent == "Checking the setup");
        drafting.StreamingContent.ShouldBeEmpty();

        call.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        call.Answer.SetResult("");
        var landed = await fixture.WaitAsync(run => run.MessageDelta?.Appends
            .Any(append => append.Message.Content == "Checking the setup") == true);
        landed.DraftContent.ShouldBeNull();

        (await fixture.NextCallAsync()).Answer.SetResult("Done");
        (await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed)).DraftContent.ShouldBeNull();
    }

    [Fact]
    public async Task ProvisionalTextAfterAToolMustNotBecomeTheFinalAnswer()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Completion.AdaptLegacyFinalAnswers = false;
        await fixture.SetPolicyAsync("Allow");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Change a file"));

        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        first.Answer.SetResult("");
        var premature = await fixture.NextCallAsync();
        premature.Answer.SetResult("I will now verify the result.");

        var corrective = await fixture.NextCallAsync();
        corrective.Request.Tools!.ShouldContain(tool => tool.Name == "mcp_built_in__process_run");
        var generating = await fixture.WaitAsync(run => run.Status == ChatRunStatus.Generating);
        generating.StreamingContent.ShouldBeEmpty();
        corrective.Request.ContextMessages!.Where(message => message.Role == "system")
            .ShouldContain(message => message.Content.Contains("has not been published yet", StringComparison.Ordinal));
        corrective.ToolCalls = [new ChatToolCall("finish-1", RunCompletionProtocol.Name, """
            {"status":"complete","finalAnswer":"Done and verified.","includePreviousText":false,"completed":["Changed and verified the file"],"evidence":["Command succeeded"],"remaining":[]}
            """)];
        corrective.Answer.SetResult("");

        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Where(message => message.Role == "Assistant" && message.ToolCalls is null)
            .ShouldHaveSingleItem().Content.ShouldBe("Done and verified.");
        chat.Messages.ShouldNotContain(message => message.Content.Contains("has not been published yet", StringComparison.Ordinal));
        chat.Messages.ShouldNotContain(message => message.Content == "I will now verify the result.");
    }

    [Fact]
    public async Task ProvisionalTextFollowedByMoreWorkMustBePublishedAsItsPreamble()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Completion.AdaptLegacyFinalAnswers = false;
        await fixture.SetPolicyAsync("Allow");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Analyze a directory"));

        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        first.Answer.SetResult("");
        (await fixture.NextCallAsync()).Answer.SetResult("## Report\n\n3 files.");
        var save = await fixture.NextCallAsync();
        save.ToolCalls = [new ChatToolCall("call-2", "mcp_built_in__process_run", "{}")];
        save.Answer.SetResult("Saving it.");
        var finish = await fixture.NextCallAsync();
        finish.Request.ContextMessages!.Where(message => message.Role == "assistant")
            .ShouldNotContain(message => message.Content == "## Report\n\n3 files.");
        finish.ToolCalls = [new ChatToolCall("finish-1", RunCompletionProtocol.Name, """
            {"status":"complete","finalAnswer":"Saved."}
            """)];
        finish.Answer.SetResult("");

        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.ShouldContain(message => message.Role == "Assistant" && message.ToolCalls != null
            && message.Content == "## Report\n\n3 files.\n\nSaving it.");
    }

    [Fact]
    public async Task CompletionMustPublishTheHeldBackTextUnlessTheModelDiscardsIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Completion.AdaptLegacyFinalAnswers = false;
        await fixture.SetPolicyAsync("Allow");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Analyze a directory"));

        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        first.Answer.SetResult("");
        (await fixture.NextCallAsync()).Answer.SetResult("## Report\n\n3 files.");
        var finish = await fixture.NextCallAsync();
        finish.Request.ContextMessages!.Where(message => message.Role == "system")
            .ShouldContain(message => message.Content.Contains("published as written", StringComparison.Ordinal));
        // As the model did: no flag, and a one-line summary claiming the report was already shown.
        finish.ToolCalls = [new ChatToolCall("finish-1", RunCompletionProtocol.Name, """
            {"status":"complete","finalAnswer":"report printed."}
            """)];
        finish.Answer.SetResult("");

        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Where(message => message.Role == "Assistant" && message.ToolCalls is null)
            .ShouldHaveSingleItem().Content.ShouldBe("## Report\n\n3 files.\n\nreport printed.");
    }

    [Fact]
    public async Task CompletionCalledWithTheAppServerPrefixMustStillEndTheRun()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Completion.AdaptLegacyFinalAnswers = false;
        await fixture.SetPolicyAsync("Allow");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "What is my name?"));

        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        first.Answer.SetResult("");
        // The model has just read App tool names and spells the control tool the same way.
        var finish = await fixture.NextCallAsync();
        finish.ToolCalls = [new ChatToolCall("finish-1", ToolRef.AppPrefix + RunCompletionProtocol.Name, """
            {"status":"complete","finalAnswer":"Your name is Kolya.","completed":["Read memory"],"evidence":["Profile entry"],"remaining":[]}
            """)];
        finish.Answer.SetResult("");

        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Where(message => message.Role == "Assistant" && message.ToolCalls is null)
            .ShouldHaveSingleItem().Content.ShouldBe("Your name is Kolya.");
    }

    [Fact]
    public async Task RepeatedToolResultsRequireAnHonestFinalDecision()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Completion.AdaptLegacyFinalAnswers = false;
        await fixture.SetPolicyAsync("Allow");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Find information"));

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var work = await fixture.NextCallAsync();
            work.ToolCalls = [new ChatToolCall($"call-{attempt}", "mcp_built_in__process_run", "{}")];
            work.Answer.SetResult("");
        }

        var finish = await fixture.NextCallAsync();
        finish.Request.Tools!.ShouldHaveSingleItem().Name.ShouldBe(RunCompletionProtocol.Name);
        finish.Request.ContextMessages!.Where(message => message.Role == "system")
            .ShouldContain(message => message.Content.Contains("no new information", StringComparison.Ordinal));
        finish.ToolCalls = [new ChatToolCall("finish-1", RunCompletionProtocol.Name, """
            {"status":"blocked","finalAnswer":"I could not find the answer with the available tools."}
            """)];
        finish.Answer.SetResult("");

        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        fixture.Tools.CallCount.ShouldBe(5);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Where(message => message.Role == "Assistant" && message.ToolCalls is null)
            .ShouldHaveSingleItem().Content.ShouldBe("I could not find the answer with the available tools.");
    }

    [Fact]
    public async Task StalledRunDoesNotExecuteAnotherToolCall()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Completion.AdaptLegacyFinalAnswers = false;
        await fixture.SetPolicyAsync("Allow");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Find information"));

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var work = await fixture.NextCallAsync();
            work.ToolCalls = [new ChatToolCall($"call-{attempt}", "mcp_built_in__process_run", "{}")];
            work.Answer.SetResult("");
        }

        var ignored = await fixture.NextCallAsync();
        ignored.Request.Tools!.ShouldHaveSingleItem().Name.ShouldBe(RunCompletionProtocol.Name);
        ignored.ToolCalls = [new ChatToolCall("call-ignored", "mcp_built_in__process_run", "{}")];
        ignored.Answer.SetResult("");

        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        fixture.Tools.CallCount.ShouldBe(5);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Where(message => message.Role == "Assistant" && message.ToolCalls is null)
            .ShouldHaveSingleItem().Content.ShouldContain("stopped after several steps");
    }

    [Fact]
    public async Task LegacyContinueDecisionIsRejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Completion.AdaptLegacyFinalAnswers = false;
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Find information"));

        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("finish-1", RunCompletionProtocol.Name, """
            {"status":"continue","nextAction":"Try again"}
            """)];
        first.Answer.SetResult("");

        var corrected = await fixture.NextCallAsync();
        corrected.Request.Tools!.ShouldHaveSingleItem().Name.ShouldBe(RunCompletionProtocol.Name);
        corrected.ToolCalls = [new ChatToolCall("finish-2", RunCompletionProtocol.Name, """
            {"status":"blocked","finalAnswer":"I cannot find the answer with the available tools."}
            """)];
        corrected.Answer.SetResult("");

        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Where(message => message.Role == "Assistant" && message.ToolCalls is null)
            .ShouldHaveSingleItem().Content.ShouldBe("I cannot find the answer with the available tools.");
    }

    [Fact]
    public async Task ProseRepeatedAfterEveryCompletionCorrectionMustBePublishedInsteadOfFailing()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Completion.AdaptLegacyFinalAnswers = false;
        await fixture.SetPolicyAsync("Allow");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "What is my name?"));

        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        first.Answer.SetResult("");
        // An endpoint that never emits the control call: the answer arrives as prose every time,
        // including when app_finish_run is the only tool offered.
        (await fixture.NextCallAsync()).Answer.SetResult("Your name is Kolya.");
        for (var correction = 0; correction < 2; correction++)
            (await fixture.NextCallAsync()).Answer.SetResult("Your name is Kolya.");
        var last = await fixture.NextCallAsync();
        last.Request.Tools!.ShouldHaveSingleItem().Name.ShouldBe(RunCompletionProtocol.Name);
        last.Answer.SetResult("Kolya.");

        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Where(message => message.Role == "Assistant" && message.ToolCalls is null)
            .ShouldHaveSingleItem().Content.ShouldBe("Kolya.");
    }

    [Fact]
    public async Task EmptyResponsesAfterProvisionalProseMustPublishThatProse()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Completion.AdaptLegacyFinalAnswers = false;
        await fixture.SetPolicyAsync("Allow");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Remember that I use DI"));

        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        first.Answer.SetResult("");
        (await fixture.NextCallAsync()).Answer.SetResult("Saved: you use DI through interfaces.");
        // Offered only the control tool, this endpoint falls silent.
        for (var attempt = 0; attempt < 3; attempt++)
            (await fixture.NextCallAsync()).Answer.SetResult("");

        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Where(message => message.Role == "Assistant" && message.ToolCalls is null)
            .ShouldHaveSingleItem().Content.ShouldBe("Saved: you use DI through interfaces.");
    }

    [Fact]
    public async Task EmptyResponseMustKeepCompletionCorrectionUntilAValidDecision()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Completion.AdaptLegacyFinalAnswers = false;
        await fixture.SetPolicyAsync("Allow");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Change a file"));

        var work = await fixture.NextCallAsync();
        work.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        work.Answer.SetResult("");

        var invalid = await fixture.NextCallAsync();
        invalid.ToolCalls = [new ChatToolCall("finish-invalid", RunCompletionProtocol.Name, """
            {"status":"complete"}
            """)];
        invalid.Answer.SetResult("");

        var empty = await fixture.NextCallAsync();
        empty.Request.Tools!.ShouldHaveSingleItem().Name.ShouldBe(RunCompletionProtocol.Name);
        empty.FinishReason = "stop";
        empty.Answer.SetResult("");

        var corrected = await fixture.NextCallAsync();
        corrected.Request.Tools!.ShouldHaveSingleItem().Name.ShouldBe(RunCompletionProtocol.Name);
        var hidden = corrected.Request.ContextMessages!.Where(message => message.Role == "system")
            .Select(message => message.Content).ToArray();
        hidden.ShouldContain(message => message.Contains("call was rejected", StringComparison.Ordinal));
        hidden.ShouldContain(message => message.Contains("last response was empty", StringComparison.Ordinal));
        corrected.ToolCalls = [new ChatToolCall("finish-valid", RunCompletionProtocol.Name, """
            {"status":"complete","finalAnswer":"Done and verified.","completed":["Changed the file"],"evidence":[],"remaining":[]}
            """)];
        corrected.Answer.SetResult("");

        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Where(message => message.Role == "Assistant" && message.ToolCalls is null)
            .ShouldHaveSingleItem().Content.ShouldBe("Done and verified.");
    }

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
    public async Task StoppingAndResumingShouldKeepLiveWorkspaceChanges()
    {
        var workspace = new TestWorkspaceChangeTracker();
        workspace.Enqueue(new WorkspaceChangeSet(
            [new FileChange("continued.cs", FileChangeKind.Modified, 2, 0, Diff: "saved diff")], 2, 0));
        await using var fixture = await Fixture.CreateAsync(workspace);
        await fixture.SetPolicyAsync("Allow");

        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Edit a file"));
        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        first.Answer.SetResult("");
        await fixture.NextCallAsync();
        await fixture.WaitAsync(run => run.WorkspaceChanges is { IsEmpty: false });

        await fixture.Dispatcher.StopAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None);
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Paused && run.WorkspaceChanges is { IsEmpty: false });
        await fixture.Dispatcher.ResumeAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None);
        var resumed = await fixture.NextCallAsync();
        var live = await fixture.WaitAsync(run => run.Status == ChatRunStatus.Generating
            && run.WorkspaceChanges is { IsEmpty: false });
        live.WorkspaceChanges!.Files.ShouldHaveSingleItem().Path.ShouldBe("continued.cs");

        resumed.Answer.SetResult("Done");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
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

        var transcript = await fixture.Chats.GetTranscriptAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        var visibleReplies = transcript!.Messages.Where(message => message.Role == "Assistant").ToArray();
        visibleReplies.Length.ShouldBe(2);
        visibleReplies[0].WorkspaceChanges!.Files.ShouldHaveSingleItem().Diff.ShouldBe("first diff");
        visibleReplies[1].WorkspaceChanges!.Files.ShouldHaveSingleItem().Diff.ShouldBe("second diff");
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
    public async Task FullAccessShouldRunAskToolsWithoutACardButKeepDeny()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Chats.UpdateApprovalModeAsync(fixture.ProjectId, fixture.ChatId,
            new UpdateChatApprovalModeRequest(ToolApprovalMode.FullAccess), CancellationToken.None);
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Run command"));
        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        first.Answer.SetResult("");

        var second = await fixture.NextCallAsync();
        fixture.Tools.CallCount.ShouldBe(1);
        second.Answer.SetResult("Done");
        var completed = await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        completed.PendingApproval.ShouldBeNull();

        await fixture.SetPolicyAsync("Deny");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Run it again"));
        var third = await fixture.NextCallAsync();
        third.ToolCalls = [new ChatToolCall("call-2", "mcp_built_in__process_run", "{}")];
        third.Answer.SetResult("");
        var fourth = await fixture.NextCallAsync();
        fixture.Tools.CallCount.ShouldBe(1);
        fourth.Answer.SetResult("Done");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
    }

    [Fact]
    public async Task SwitchingToFullAccessShouldAnswerTheWaitingCard()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Run command"));
        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_built_in__process_run", "{}")];
        first.Answer.SetResult("");
        await fixture.WaitAsync(run => run.PendingApproval is not null);

        await fixture.Chats.UpdateApprovalModeAsync(fixture.ProjectId, fixture.ChatId,
            new UpdateChatApprovalModeRequest(ToolApprovalMode.FullAccess), CancellationToken.None);

        var second = await fixture.NextCallAsync();
        fixture.Tools.CallCount.ShouldBe(1);
        second.Answer.SetResult("Done");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
    }

    [Fact]
    public async Task ToolLimitShouldIdentifyTheToolPolicyScopeAndKeepOtherToolsAvailable()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetPolicyAsync("Allow", maxCalls: 1);
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Run twice"));

        var first = await fixture.NextCallAsync();
        first.ToolCalls =
        [
            new ChatToolCall("call-1", "mcp_built_in__process_run", "{}"),
            new ChatToolCall("call-2", "mcp_built_in__process_run", "{}")
        ];
        first.Answer.SetResult("");

        var next = await fixture.NextCallAsync();
        fixture.Tools.CallCount.ShouldBe(1);
        var limited = next.Request.ContextMessages!.Where(message => message.Role == "tool").Last();
        limited.ModelContent.ShouldNotBeNull();
        limited.ModelContent!.ShouldContain("Only this tool is limited for the current run");
        limited.ModelContent.ShouldContain("other available tools remain usable");
        var result = fixture.Codec.Read(limited.Content);
        result.IsError.ShouldBeTrue();
        result.StructuredContent!.Value.GetProperty("code").GetString().ShouldBe("tool_call_limit_reached");
        result.StructuredContent.Value.GetProperty("tool").GetString().ShouldBe("mcp_built_in__process_run");
        result.StructuredContent.Value.GetProperty("count").GetInt32().ShouldBe(2);
        result.StructuredContent.Value.GetProperty("limit").GetInt32().ShouldBe(1);
        result.StructuredContent.Value.GetProperty("scope").GetString().ShouldBe("project");
        result.StructuredContent.Value.GetProperty("limitedToolOnly").GetBoolean().ShouldBeTrue();
        result.StructuredContent.Value.GetProperty("otherToolsAvailable").GetBoolean().ShouldBeTrue();

        next.Answer.SetResult("Done");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
    }

    [Fact]
    public async Task QuestionShouldStopTheRunAndCarryTheAnswerBack()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Tools.Broker = fixture.Broker;
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Refactor it"));
        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_app__ask_user", "{}")];
        first.Answer.SetResult("");

        // No confirmation card on the way in: asking to be allowed to ask would put the same
        // decision to the same person twice.
        var waiting = await fixture.WaitAsync(run => run.PendingPrompt is not null);
        waiting.PendingApproval.ShouldBeNull();
        var prompt = waiting.PendingPrompt!;
        prompt.Questions.ShouldHaveSingleItem().Options.Count.ShouldBe(2);

        (await fixture.Dispatcher.AnswerPromptAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId,
            new UserPromptResponse(prompt.Id, UserPromptOutcome.Answered,
                [new UserPromptAnswer("scope", [1], null)]), CancellationToken.None)).ShouldBeTrue();

        var second = await fixture.NextCallAsync();
        second.Answer.SetResult("Done");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);

        fixture.Tools.LastResponse!.Outcome.ShouldBe(UserPromptOutcome.Answered);
        fixture.Tools.LastResponse.Answers.ShouldHaveSingleItem().Selected.ShouldBe([1]);
        // The card belongs to the question, not to the chat: once answered there is nothing to show.
        (await fixture.WaitAsync(run => run.PendingPrompt is null)).PendingPrompt.ShouldBeNull();
    }

    [Fact]
    public async Task AnswerToAQuestionThatIsNoLongerCurrentShouldBeRefused()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Tools.Broker = fixture.Broker;
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Refactor it"));
        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_app__ask_user", "{}")];
        first.Answer.SetResult("");
        var prompt = (await fixture.WaitAsync(run => run.PendingPrompt is not null)).PendingPrompt!;

        // A card left open in another window names a prompt that has moved on; answering the
        // question that replaced it is not what that click meant.
        (await fixture.Dispatcher.AnswerPromptAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId,
            new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Answered, []), CancellationToken.None)).ShouldBeFalse();
        (await fixture.Dispatcher.AnswerPromptAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId,
            new UserPromptResponse(prompt.Id, UserPromptOutcome.Dismissed, []), CancellationToken.None)).ShouldBeTrue();

        var second = await fixture.NextCallAsync();
        second.Answer.SetResult("Done");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        fixture.Tools.LastResponse!.Outcome.ShouldBe(UserPromptOutcome.Dismissed);
    }

    [Fact]
    public async Task StoppingTheRunShouldReleaseTheQuestionWithIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Tools.Broker = fixture.Broker;
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Refactor it"));
        var first = await fixture.NextCallAsync();
        first.ToolCalls = [new ChatToolCall("call-1", "mcp_app__ask_user", "{}")];
        first.Answer.SetResult("");
        await fixture.WaitAsync(run => run.PendingPrompt is not null);

        await fixture.Dispatcher.StopAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None);

        // Whoever was waiting on the answer is gone, so the card cannot be left behind for someone
        // to answer into nothing.
        (await fixture.WaitAsync(run => run.PendingPrompt is null)).PendingPrompt.ShouldBeNull();
    }

    [Fact]
    public async Task BackgroundRunShouldBeToldAtOnceThatNobodyIsThere()
    {
        await using var fixture = await Fixture.CreateAsync();
        var response = await ((IUserPromptBroker)fixture.Dispatcher).AskAsync(
            new ToolRunContext(fixture.ProjectId, fixture.ChatId, fixture.ChatId, Interactive: false),
            new UserPromptRequest([new UserPromptQuestion("q", "Which?", null, [], false, true)]),
            TimeSpan.FromMinutes(15), CancellationToken.None);

        response.Outcome.ShouldBe(UserPromptOutcome.Interrupted);
        response.Answers.ShouldBeEmpty();
    }

    [Fact]
    public async Task GlobalToolPolicyShouldApplyWithoutProjectSettings()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetGlobalPolicyAsync("Allow");
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Run command"));
        var first = await fixture.NextCallAsync();
        first.Request.Tools!.Count.ShouldBe(2);
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
        first.Request.Tools!.Count.ShouldBe(decision == "Deny" ? 1 : 2);
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
        fixture.Context.Build(restored, completed.HeadMessageId!.Value).Select(message => message.Role).ShouldBe(["user", "assistant", "tool", "assistant"]);
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
        call.Request.Tools!.Count.ShouldBe(2);
        fixture.Tools.OpenCount.ShouldBe(1);
        call.Answer.SetResult("Reply");
        var completed = await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        completed.Queue.ShouldBeEmpty();
        completed.ChatRevision.ShouldBeGreaterThan(first.ChatRevision);
        completed.HeadMessageId.ShouldNotBeNull();
    }

    [Fact]
    public async Task OversizedContextShouldFailBeforeCallingTransport()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.SubmitAsync(new SubmitChatMessageRequest(
            Guid.NewGuid(), Guid.NewGuid(), new string('x', 60_000)));

        var failed = await fixture.WaitAsync(run => run.Status == ChatRunStatus.Failed);
        failed.Error.ShouldNotBeNull().ShouldContain("too large for the model context window");
        failed.FailureCode.ShouldBe(RunFailureCode.ContextWindow);
        failed.CanRetry.ShouldBeTrue();
        fixture.Completion.Calls.Reader.TryRead(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task ConnectionContextOverrideShouldReachPlanner()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SetConnectionLimitsAsync(65_536, 8_192);

        await fixture.SubmitAsync(new SubmitChatMessageRequest(
            Guid.NewGuid(), Guid.NewGuid(), new string('x', 60_000)));

        var call = await fixture.NextCallAsync();
        call.Request.CredentialProfileId.ShouldNotBeNull();
        call.Answer.SetResult("Fits configured context");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
    }

    [Fact]
    public async Task LongHistoryShouldBeCompactedOnlyForTransport()
    {
        await using var fixture = await Fixture.CreateAsync();
        var requests = Enumerable.Range(1, 6)
            .Select(index => $"request-{index} " + new string((char)('a' + index), 10_000))
            .ToArray();
        Call? last = null;
        foreach (var request in requests)
        {
            await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), request));
            last = await fixture.NextCallAsync();
            last.Answer.SetResult("done");
            await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);
        }

        last.ShouldNotBeNull();
        var visibleContext = last.Request.ContextMessages!.Where(message => message.Role != "system").ToArray();
        visibleContext[0].Role.ShouldBe("user");
        visibleContext[0].Content.ShouldStartWith("Earlier conversation summary");
        visibleContext[^1].Content.ShouldBe(requests[^1]);

        var stored = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        stored.ShouldNotBeNull();
        stored.Messages.Where(message => message.Role == "User").Select(message => message.Content)
            .ShouldBe(requests);
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
        fork.Request.ContextMessages!.Where(item => item.Role != "system").Select(item => item.Content)
            .ShouldBe(["Original", "Alternative"]);
        fork.Answer.SetResult("Alternative reply");
        await fixture.WaitAsync(run => run.BranchId == forkId && run.Status == ChatRunStatus.Completed);
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Branches!.Single(branch => branch.Id == fixture.ChatId).HeadMessageId.ShouldBe(main.HeadMessageId);
    }

    [Fact]
    public async Task EditingRootIntoForkShouldCreateAnIndependentRootBranch()
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), original, "Original root"));
        (await fixture.NextCallAsync()).Answer.SetResult("Original reply");
        var main = await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);

        var forkId = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), forkId, "Alternative root",
            ChatSubmitMode.Fork, BranchId: fixture.ChatId, ParentMode: MessageParentMode.Root));
        var fork = await fixture.NextCallAsync();
        fork.Request.ContextMessages!.Where(message => message.Role != "system").Select(message => message.Content)
            .ShouldBe(["Alternative root"]);
        fork.Answer.SetResult("Alternative reply");
        await fixture.WaitAsync(run => run.BranchId == forkId && run.Status == ChatRunStatus.Completed);

        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        var branches = chat!.Branches!;
        branches.Single(branch => branch.Id == fixture.ChatId).HeadMessageId.ShouldBe(main.HeadMessageId);
        fixture.Context.Build(chat, branches.Single(branch => branch.Id == forkId).HeadMessageId!.Value)
            .Select(message => message.Content).ShouldBe(["Alternative root", "Alternative reply"]);
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
        call.Request.ContextMessages!.Where(message => message.Role != "system").Select(message => message.Content)
            .ShouldBe(["Replacement"]);
        call.Answer.SetResult("Replacement reply");
        await fixture.WaitAsync(run => run.BranchId == fixture.ChatId && run.Status == ChatRunStatus.Completed);

        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        var branches = chat!.Branches!;
        fixture.Context.Build(chat, branches.Single(branch => branch.Id == fixture.ChatId).HeadMessageId!.Value)
            .Select(message => message.Content).ShouldBe(["Replacement", "Replacement reply"]);
        fixture.Context.Build(chat, branches.Single(branch => branch.Id == forkId).HeadMessageId!.Value)
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
        fixture.Context.Build(chat, child.HeadMessageId!.Value).Select(message => message.Content)
            .ShouldBe(["Root", "Parent", "Child", "Child reply"]);

        var childRun = (await fixture.Dispatcher.GetSnapshotAsync(CancellationToken.None))
            .Single(run => run.BranchId == childId);
        childRun.Queue.ShouldHaveSingleItem().Id.ShouldBe(queuedId);
        await fixture.Dispatcher.ResumeAsync(fixture.ProjectId, fixture.ChatId, childId, CancellationToken.None);
        var resumed = await fixture.NextCallAsync();
        resumed.Request.ContextMessages!.Where(message => message.Role != "system").Select(message => message.Content)
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
    public async Task StoppingShouldNotPublishProvisionalModelText()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StreamPreludeAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Question"), "Half an");

        await fixture.Dispatcher.StopAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None, Guid.NewGuid());
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Paused);

        // Text is not user-visible until the provider has completed a publishable answer. A
        // fragment interrupted mid-stream must not become either an ordinary or an incomplete
        // assistant message.
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.ShouldNotContain(message => message.Role == "Assistant");

        // The command is still queued, because Resume is expected to rebuild it - but it is no
        // longer the active one, so nothing is left claiming to be running.
        var paused = await fixture.WaitAsync(run => run.Status == ChatRunStatus.Paused && run.ActiveMessageId is null);
        paused.Queue.ShouldHaveSingleItem().Stage.ShouldBe(QueuedMessageStage.UserCommitted);
    }

    [Fact]
    public async Task ResumingAfterAnUnpublishedFragmentShouldProduceOneAnswer()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StreamPreludeAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Question"), "Half an");
        await fixture.Dispatcher.StopAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None, Guid.NewGuid());
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Paused);

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
        chat!.Messages.ShouldNotContain(message => message.Content == "Thinking");
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
        chat!.Messages.ShouldNotContain(message => message.Content == "Thinking");
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
    public async Task ReplacingAnActiveBranchAfterToolBatchesShouldDropItsOldQueueAndStartReplacement()
    {
        await using var fixture = await Fixture.CreateAsync();
        var sourceId = await StartRunAfterTwoToolBatchesAsync(fixture);
        var abandonedId = Guid.NewGuid();
        await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), abandonedId, "Abandoned queued message"));

        var replacementId = Guid.NewGuid();
        var replacing = await fixture.SubmitAsync(new SubmitChatMessageRequest(Guid.NewGuid(), replacementId,
            "Replacement", ChatSubmitMode.Replace, BranchId: fixture.ChatId, ReplaceSourceId: sourceId));

        replacing.Queue.Select(item => item.Id).ShouldBe([replacementId]);
        var replacement = await fixture.NextCallAsync();
        AssertValidToolContext(replacement.Request.ContextMessages!, "Replacement");
        replacement.Answer.SetResult("Replacement reply");
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Completed);

        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        chat!.Messages.Select(message => message.Content).ShouldBe(["Replacement", "Replacement reply"]);
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
    public async Task InterruptedRunShouldResumeCleanlyAcrossRestart()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StreamPreludeAsync(new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Question"), "Half an");
        await fixture.Dispatcher.StopAsync(fixture.ProjectId, fixture.ChatId, fixture.ChatId, CancellationToken.None, Guid.NewGuid());
        await fixture.WaitAsync(run => run.Status == ChatRunStatus.Paused);
        await fixture.RestartAsync();

        // The unpublished provider fragment must not satisfy the "this command already has its
        // reply" check, or resuming would quietly close the command without generating anything.
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
        rest.Request.ContextMessages!.Where(message => message.Role == "system")
            .ShouldContain(message => message.Content.Contains("cut off at the output token limit", StringComparison.Ordinal));
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
        private static readonly string[] TestCompleted = ["Completed the test scenario"];
        private TaskCompletionSource<bool>? _cancellationObserved;
        private TaskCompletionSource<bool>? _cancellationRelease;
        public Channel<Call> Calls { get; } = Channel.CreateUnbounded<Call>();

        /// <summary>Text the next call streams before it is answered; consumed once.</summary>
        public string? NextPrelude { get; set; }

        /// <summary>What the skill router answers; nowhere by default.</summary>
        public string RouteAnswer { get; set; } = "{}";
        public bool AdaptLegacyFinalAnswers { get; set; } = true;
        public Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(ChatCompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Skill routing asks the model once before a turn's first step. It routes nowhere here, so
            // every scenario scripts only the chat model's own steps.
            if (request.Message == "Route the request")
            {
                yield return new ChatCompletionChunk(RouteAnswer);
                yield break;
            }
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
            var toolCalls = call.ToolCalls;
            if (AdaptLegacyFinalAnswers && toolCalls is null && !string.IsNullOrWhiteSpace(content)
                && request.ContextMessages?.Any(message => message.Role == "tool") == true
                && request.Tools?.Any(tool => tool.Name == RunCompletionProtocol.Name) == true)
            {
                toolCalls = [new ChatToolCall($"finish-{Guid.NewGuid():N}", RunCompletionProtocol.Name,
                    JsonSerializer.Serialize(new
                    {
                        status = "complete",
                        finalAnswer = content,
                        completed = TestCompleted,
                        evidence = Array.Empty<string>(),
                        remaining = Array.Empty<string>()
                    }))];
                content = "";
            }
            if (toolCalls is { Count: > 0 })
                yield return new ChatCompletionChunk("", ToolCallsStarted: true);
            yield return new ChatCompletionChunk(content, ToolCalls: toolCalls, FinishReason: call.FinishReason);
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
        private ChatExecutionComposition _composition;
        public MemoryFileSystem FileSystem { get; } = new();
        public Completion Completion { get; } = new();
        public TestTools Tools { get; } = new();
        public IWorkspaceChangeTracker Workspace { get; }
        public IChatService Chats => _composition.Resolve<IChatService>();
        public IChatRunDispatcher Dispatcher => _composition.Resolve<IChatRunDispatcher>();
        public IChatContextBuilder Context => _composition.Resolve<IChatContextBuilder>();
        public IUserPromptBroker Broker => _composition.Resolve<IUserPromptBroker>();
        public IToolResultCodec Codec => _composition.Resolve<IToolResultCodec>();
        private IProjectService Projects => _composition.Resolve<IProjectService>();
        private IGlobalSettingsRepository Settings => _composition.Resolve<IGlobalSettingsRepository>();
        private IChatRepository ChatRepository => _composition.Resolve<IChatRepository>();
        private IClock Clock => _composition.Resolve<IClock>();
        public Guid ProjectId { get; private set; }
        public Guid ChatId { get; private set; }
        private Fixture(IWorkspaceChangeTracker? workspace = null)
        {
            Workspace = workspace ?? new WorkspaceChangeTracker(new LineDiff());
            _composition = NewComposition();
        }

        /// <summary>
        /// The shipped server graph over this fixture's memory. A new one over the same memory is
        /// what a restart of the Host amounts to.
        /// </summary>
        private ChatExecutionComposition NewComposition() => new(
            options: new ServerOptions("data", null, true),
            fileSystem: FileSystem,
            completion: Completion,
            tools: Tools,
            workspace: Workspace);

        public static async Task<Fixture> CreateAsync(IWorkspaceChangeTracker? workspace = null)
        {
            var fixture = new Fixture(workspace);
            // A drafted reply is one more call to the scripted model after every answer, which these
            // tests read call by call; the drafts have tests of their own.
            await fixture.Settings.SaveAsync(new GlobalSettings([new ConnectionSettings(Guid.NewGuid(), "Test", "https://example.test/v1", "model", true, true, false)], [], [],
                new ChatAutomationSettings(SuggestReplies: false)), CancellationToken.None);
            fixture.ProjectId = (await fixture.Projects.CreateAsync(new CreateProjectRequest("Test", ""), CancellationToken.None)).Id;
            fixture.ChatId = (await fixture.Chats.CreateAsync(fixture.ProjectId, new CreateChatRequest("Chat"), CancellationToken.None)).Id;
            await fixture.Dispatcher.WarmUpAsync(CancellationToken.None);
            return fixture;
        }
        public Task<ChatRunSnapshot> SubmitAsync(SubmitChatMessageRequest request) => Dispatcher.SubmitAsync(ProjectId, ChatId, request, CancellationToken.None);

        public async Task AddGrantAsync(string root)
        {
            var project = (await Projects.GetAsync(ProjectId, CancellationToken.None))!;
            var result = await Projects.AddDirectoryGrantAsync(ProjectId, project.Revision,
                new DirectoryGrantSettings(Guid.NewGuid(), "sample", root, true, ["read"]), CancellationToken.None);
            result.Status.ShouldBe(ProjectUpdateStatus.Updated);
        }
        public async Task SetConnectionLimitsAsync(long contextWindowTokens, long reservedOutputTokens)
        {
            var current = await Settings.LoadAsync(CancellationToken.None);
            await Settings.SaveAsync(current with
            {
                Connections = current.Connections.Select(connection => connection with
                {
                    ContextWindowTokens = contextWindowTokens,
                    ReservedOutputTokens = reservedOutputTokens
                }).ToArray()
            }, CancellationToken.None);
        }
        public async Task AppendLegacyReplacementAsync(Guid sourceId, Guid replacementId, string content)
        {
            var stored = await ChatRepository.GetAsync(new Domain.Projects.ProjectId(ProjectId),
                new Domain.Chats.ChatId(ChatId), CancellationToken.None) ?? throw new InvalidOperationException("Chat not found.");
            var source = stored.Chat.Messages.Single(message => message.Id.Value == sourceId);
            stored.Chat.ReplaceInBranch(ChatId, source.Id,
                new Domain.Chats.ChatMessage(new Domain.Chats.ChatMessageId(replacementId), source.ParentId,
                    Domain.Chats.ChatMessageRole.User, content, Clock.UtcNow), Clock.UtcNow);
            var result = await ChatRepository.SaveAsync(stored.Chat, stored.Revision, CancellationToken.None);
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

        public Task<ProjectDetails?> GetProjectAsync() => Projects.GetAsync(ProjectId, CancellationToken.None);
        public Task<GlobalSettings> GetGlobalAsync() => Settings.LoadAsync(CancellationToken.None);
        public IChatReplySuggestions ReplySuggestions => _composition.Resolve<IChatReplySuggestions>();
        public async Task SetChatAutomationAsync(ChatAutomationSettings automation)
        {
            var global = await Settings.LoadAsync(CancellationToken.None);
            await Settings.SaveAsync(global with { ChatAutomation = automation }, CancellationToken.None);
        }
        public async Task SetGlobalPolicyAsync(string decision)
        {
            var global = await Settings.LoadAsync(CancellationToken.None);
            await Settings.SaveAsync(global with
            {
                McpServers = [DefaultMcpServer.Settings with { Policy = "Allow" }],
                ToolPolicies = [new McpToolPolicySettings(DefaultMcpServer.Id, "process_run", "schema", decision, 20, 120)]
            }, CancellationToken.None);
        }

        public async Task SetPolicyAsync(string decision, long timeoutSeconds = 120, int maxCalls = 20)
        {
            var global = await Settings.LoadAsync(CancellationToken.None);
            await Settings.SaveAsync(global with { McpServers = [DefaultMcpServer.Settings with { Policy = "Allow" }] }, CancellationToken.None);
            var project = await Projects.GetAsync(ProjectId, CancellationToken.None);
            await Projects.UpdateSecurityAsync(ProjectId, new UpdateProjectSecurityRequest(project!.Revision, [],
                [new Contracts.Projects.McpServerSettings(DefaultMcpServer.Id, "Default", "Stdio", true)],
                [new ToolPolicySettings(DefaultMcpServer.Id, "process_run", "schema", decision, maxCalls, timeoutSeconds)]), CancellationToken.None);
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
            return call;
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
            await _composition.DisposeAsync();
            _composition = NewComposition();
            await Dispatcher.WarmUpAsync(CancellationToken.None);
        }
        public ValueTask DisposeAsync() => _composition.DisposeAsync();
    }

    private sealed class TestWorkspaceChangeTracker : IWorkspaceChangeTracker
    {
        private readonly Queue<WorkspaceChangeSet> _queued = new();
        private readonly Dictionary<WorkspaceRunKey, WorkspaceChangeSet> _current = new();

        public void Enqueue(WorkspaceChangeSet changes) => _queued.Enqueue(changes);

        public Task BeginRunAsync(WorkspaceRunKey run, IReadOnlyList<ToolDirectoryGrant> grants, WorkspaceRunKey? parent,
            CancellationToken cancellationToken)
        {
            if (!_current.ContainsKey(run))
                _current[run] = _queued.TryDequeue(out var changes) ? changes : WorkspaceChangeSet.Empty;
            return Task.CompletedTask;
        }

        public Task RecordIntentAsync(WorkspaceRunKey run, ToolDescriptor tool, string arguments, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task UpdateGrantsAsync(WorkspaceRunKey run, IReadOnlyList<ToolDirectoryGrant> grants, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RecordEffectAsync(WorkspaceRunKey run, ToolDescriptor tool, string arguments, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<WorkspaceChangeSet> SnapshotAsync(WorkspaceRunKey run, CancellationToken cancellationToken) =>
            Task.FromResult(_current.GetValueOrDefault(run, WorkspaceChangeSet.Empty));

        public Task CompleteRunAsync(WorkspaceRunKey run, CancellationToken cancellationToken)
        {
            _current.Remove(run);
            return Task.CompletedTask;
        }
    }

    private sealed class TestTools : IToolSessionFactory, IToolSession
    {
        // A stand-in server answers in the stored result format, so it needs the codec itself.
        private readonly ToolResultCodec _codec = new(new ToolResultModelProjector());
        public int CallCount { get; private set; }
        public int OpenCount { get; private set; }
        private static readonly AgentTool ProcessRun = new(
            new ChatToolDefinition("mcp_built_in__process_run", "Run", JsonSerializer.Deserialize<JsonElement>("{}")),
            ToolDescriptor.Basic("mcp_built_in__process_run", "process_run", "Run", JsonSerializer.Deserialize<JsonElement>("{}")),
            DefaultMcpServer.Id, "process_run", "schema");

        /// <summary>
        /// Stands in for the real ask_user: same server, same name, and it reaches the person the
        /// same way — through the broker — so the run's side of a question is exercised here rather
        /// than only inside the tool that asks it. Offered only to a test that set a broker, so
        /// every other test still sees the one tool it was written against.
        /// </summary>
        private static readonly AgentTool AskUser = new(
            new ChatToolDefinition("mcp_app__ask_user", "Ask", JsonSerializer.Deserialize<JsonElement>("{}")),
            ToolDescriptor.Basic("mcp_app__ask_user", "ask_user", "Ask", JsonSerializer.Deserialize<JsonElement>("{}")),
            AppMcpServer.Id, "ask_user", "schema");

        /// <summary>Stands in for app_run_skill, offered only to a test that set <see cref="OfferRunSkill"/>.</summary>
        private static readonly AgentTool RunSkill = new(
            new ChatToolDefinition("mcp_app__run_skill", "Run a skill", JsonSerializer.Deserialize<JsonElement>("{}")),
            ToolDescriptor.Basic("mcp_app__run_skill", "run_skill", "Run a skill", JsonSerializer.Deserialize<JsonElement>("{}")),
            AppMcpServer.Id, "run_skill", "schema");

        public bool OfferRunSkill { get; set; }

        /// <summary>The arguments of every run_skill call, in order.</summary>
        public List<string> SkillRuns { get; } = [];

        public IReadOnlyList<AgentTool> Tools =>
            [ProcessRun, .. Broker is null ? Array.Empty<AgentTool>() : [AskUser], .. OfferRunSkill ? [RunSkill] : Array.Empty<AgentTool>()];

        /// <summary>Set to route an ask_user call to the run that is waiting on it.</summary>
        public IUserPromptBroker? Broker { get; set; }

        /// <summary>The last response a question came back with, for the test to inspect.</summary>
        public UserPromptResponse? LastResponse { get; private set; }
        public IReadOnlyList<ToolDirectoryGrant> Grants { get; private set; } = [];
        public IReadOnlySet<Guid> Servers { get; private set; } = new HashSet<Guid>();
        public ToolRunContext? Run { get; private set; }
        public Task<IToolSession> OpenAsync(IReadOnlyList<ToolDirectoryGrant> directoryGrants, IReadOnlySet<Guid> servers,
            ToolRunContext run, CancellationToken cancellationToken)
        {
            OpenCount++;
            Grants = directoryGrants;
            Servers = servers;
            Run = run;
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
            if (tool.OriginalName == "run_skill")
            {
                SkillRuns.Add(arguments);
                return _codec.Read("{\"structuredContent\":{\"status\":\"Completed\",\"output\":{\"kind\":\"playbook\","
                    + "\"instructions\":\"1. Summarize the chat.\"}}}");
            }
            if (tool.OriginalName == "ask_user" && Broker is { } broker && Run is { } run)
            {
                LastResponse = await broker.AskAsync(run,
                    new UserPromptRequest([new UserPromptQuestion("scope", "How far?", "Scope",
                        [new UserPromptOption("Narrow", null), new UserPromptOption("Wide", null)], false, true)]),
                    TimeSpan.FromSeconds(30), cancellationToken);
                return _codec.Read("{\"structuredContent\":{\"outcome\":\""
                    + LastResponse.Outcome.ToString().ToLowerInvariant() + "\"}}");
            }

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

            return _codec.Read("{\"structuredContent\":{\"exitCode\":0}}");
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
