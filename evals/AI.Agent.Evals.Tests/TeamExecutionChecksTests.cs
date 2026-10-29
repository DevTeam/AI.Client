namespace AI.Agent.Evals.Tests;

using AI.Agent.Evals;
using AI.Contracts.Chats;
using AI.Contracts.Runs;
using Shouldly;
using Xunit;

public sealed class TeamExecutionChecksTests
{
    [Fact]
    public void FinalAnswerComesFromTheLeadEvenWhenATeammateWroteLater()
    {
        var chatId = Guid.NewGuid();
        var lead = Guid.NewGuid();
        var child = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var chat = new ChatDetails(chatId, Guid.NewGuid(), "Team", now, now, 1, null,
            [new(lead, null, "Assistant", "Integrated result", now),
                new(child, lead, "Assistant", "One module only", now)],
            [new(chatId, lead, "Lead"), new(Guid.NewGuid(), child, "Ada · Backend", Member: new("Ada", "Backend"))]);

        AttemptRunner.FinalAnswer(chat).ShouldBe("Integrated result");
    }

    [Fact]
    public void FinishedLeadDoesNotFinishAnAttemptWhileATeammateIsWorkingOrWaiting()
    {
        var lead = Run(ChatRunStatus.Completed);
        foreach (var state in new[] { ChatRunStatus.Generating, ChatRunStatus.Paused })
            AttemptRunner.AllBranchesSettled([lead, Run(state)]).ShouldBeFalse();

        var queued = Run(ChatRunStatus.Idle) with
        {
            Queue = [new(Guid.NewGuid(), "Continue", DateTimeOffset.UtcNow)]
        };
        AttemptRunner.AllBranchesSettled([lead, queued]).ShouldBeFalse();
        AttemptRunner.AllBranchesSettled([lead, Run(ChatRunStatus.Completed)]).ShouldBeTrue();
        AttemptRunner.AllBranchesSettled([lead, queued with { Status = ChatRunStatus.Failed }]).ShouldBeTrue();
    }

    [Fact]
    public async Task TeammateDelegationCannotBeProvenByAnInheritedLeadCall()
    {
        var root = Guid.NewGuid();
        var fork = Guid.NewGuid();
        var child = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var lead = new ChatMessageView(root, null, "Assistant", "", now,
            ToolCalls: [new("lead-call", "mcp_app__spawn_subtask", "{\"tasks\":[{}]}")]);
        var charter = new ChatMessageView(fork, root, "User", "Backend assignment", now);
        var report = new ChatMessageView(child, fork, "Assistant", "Done", now);
        var chat = new ChatDetails(Guid.NewGuid(), Guid.NewGuid(), "Team", now, now, 1, null,
            [lead, charter, report],
            [new(branch, child, "Ada · Backend", RootMessageId: fork, Member: new("Ada", "Backend"))]);

        AttemptRunner.TeammateTranscript(chat).ShouldBeEmpty();
        report = report with { ToolCalls = [new("child-call", "mcp_app__spawn_subtask", "{\"tasks\":[{},{}]}")] };
        chat = chat with { Messages = [lead, charter, report] };
        AttemptRunner.TeammateTranscript(chat).ShouldBeEmpty();
        var resultId = Guid.NewGuid();
        chat = chat with
        {
            Messages = [lead, charter, report,
                new(resultId, child, "Tool", "{\"completed\":true}", now, ToolCallId: "child-call")],
            Branches = [chat.Branches![0] with { HeadMessageId = resultId }]
        };
        var transcript = AttemptRunner.TeammateTranscript(chat);
        transcript.ShouldHaveSingleItem().ShouldStartWith("ROLE Backend\nCALL mcp_app__spawn_subtask");

        var directory = Directory.CreateTempSubdirectory("team-execution-checks-");
        try
        {
            var workspace = Workspace.Snapshot(directory.FullName);
            Check[] completion = [new(CheckType.TeammateTranscriptMatches,
                Pattern: "(?s)^ROLE Backend\\nCALL.*spawn_subtask.*RESULT.*completed.*true")];
            var facts = new AttemptFacts(null, 1, 0, 0, directory.FullName,
                ToolTranscript: ["CALL mcp_app__spawn_subtask {}\nRESULT {\"completed\":true}"]);
            (await Checks.RunAsync(completion, workspace, workspace, [], facts, TestContext.Current.CancellationToken))
                .ShouldHaveSingleItem().Passed.ShouldBeFalse();
            facts = facts with { TeammateTranscript = transcript };
            (await Checks.RunAsync(completion, workspace, workspace, [], facts, TestContext.Current.CancellationToken))
                .ShouldHaveSingleItem().Passed.ShouldBeTrue();
            facts = facts with { TeammateTranscript = transcript.Select(entry => entry.Replace("ROLE Backend", "ROLE Client", StringComparison.Ordinal)).ToArray() };
            (await Checks.RunAsync(completion, workspace, workspace, [], facts, TestContext.Current.CancellationToken))
                .ShouldHaveSingleItem().Passed.ShouldBeFalse();
            facts = facts with { TeammateTranscript = transcript.Select(entry => entry.Replace("true", "false", StringComparison.Ordinal)).ToArray() };
            (await Checks.RunAsync(completion, workspace, workspace, [], facts, TestContext.Current.CancellationToken))
                .ShouldHaveSingleItem().Passed.ShouldBeFalse();
        }
        finally { directory.Delete(); }
    }

    private static ChatRunSnapshot Run(ChatRunStatus status) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), status, "", [], false, null, 1);
}
