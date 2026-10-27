namespace AI.Agent.Evals.Tests;

using AI.Agent.Evals;
using AI.Contracts.Projects;
using AI.Contracts.Runs;
using AI.Contracts.Chats;
using AI.Contracts.Chat;
using Shouldly;
using Xunit;

public sealed class CliSetupChecksTests
{
    [Fact]
    public void DirectoryPickerAnswersCarryEverySelectedPath()
    {
        var prompt = new UserPrompt(Guid.NewGuid(),
            [new("paths", "Which directories?", "Directories", [], false, true, "directories")], 60);

        var response = AttemptRunner.Answer(prompt,
            [new(".*", Paths: ["/fixture/home", "/fixture/config"])]);

        response.Outcome.ShouldBe(UserPromptOutcome.Answered);
        response.Answers.ShouldHaveSingleItem().Paths.ShouldBe(["/fixture/home", "/fixture/config"]);
    }

    [Fact]
    public void DismissingThePickerDoesNotSubmitItsRecommendedDirectories()
    {
        var prompt = new UserPrompt(Guid.NewGuid(),
            [new("paths", "Which directories?", null, [new("Grant all", null, true)], false, true, "directories")], 60);

        var response = AttemptRunner.Answer(prompt, [new(".*", Dismiss: true)]);

        response.Outcome.ShouldBe(UserPromptOutcome.Dismissed);
        response.Answers.ShouldBeEmpty();
    }

    [Fact]
    public async Task GrantChecksUseTheStoredProjectAndCannotPassWithoutASnapshot()
    {
        var directory = Directory.CreateTempSubdirectory("cli-eval-checks-");
        try
        {
            var workspace = Workspace.Snapshot(directory.FullName);
            Check[] checks = [new(CheckType.ProjectGrantCount, Value: 1),
                new(CheckType.ProjectGrantMatches, Pattern: "config.*read")];
            var facts = new AttemptFacts(null, 0, 0, 0, directory.FullName);
            (await Checks.RunAsync(checks, workspace, workspace, [], facts, CancellationToken.None)).ShouldAllBe(result => !result.Passed);

            facts = facts with { ProjectGrants = [new DirectoryGrantSettings(Guid.NewGuid(), "config", "/config", true, ["read"])] };
            (await Checks.RunAsync(checks, workspace, workspace, [], facts, CancellationToken.None)).ShouldAllBe(result => result.Passed);
        }
        finally { directory.Delete(); }
    }

    [Fact]
    public async Task TranscriptCheckRequiresAnActualSuccessResultAfterTheGrant()
    {
        var directory = Directory.CreateTempSubdirectory("cli-eval-transcript-");
        try
        {
            var workspace = Workspace.Snapshot(directory.FullName);
            Check[] checks = [new(CheckType.ToolTranscriptMatches,
                Pattern: "(?s)CALL app_security.*AddDirectoryGrants.*CALL process_run_sandboxed.*RESULT.*exitCode.*0.*CLI_OK")];
            var facts = new AttemptFacts(null, 2, 0, 0, directory.FullName, ToolTranscript:
                ["CALL app_security AddDirectoryGrants", "CALL process_run_sandboxed"]);
            (await Checks.RunAsync(checks, workspace, workspace, [], facts, CancellationToken.None))[0].Passed.ShouldBeFalse();
            facts = facts with { ToolTranscript = [.. facts.ToolTranscript!, "RESULT {\"exitCode\":0,\"stdout\":\"CLI_OK\"}"] };
            (await Checks.RunAsync(checks, workspace, workspace, [], facts, CancellationToken.None))[0].Passed.ShouldBeTrue();
        }
        finally { directory.Delete(); }
    }

    [Fact]
    public async Task ToolNamesMentionedInPlanEvidenceAreNotActualPermissionCalls()
    {
        var directory = Directory.CreateTempSubdirectory("cli-eval-names-");
        try
        {
            var workspace = Workspace.Snapshot(directory.FullName);
            Check[] checks = [new(CheckType.MaxMatchingCalls, Pattern: @"^\S*__app_security\s.*AddDirectoryGrants", Value: 0),
                new(CheckType.CallsNotMatch, Pattern: @"^\S*__process_run\s")];
            var facts = new AttemptFacts(null, 1, 0, 0, directory.FullName,
                Calls: ["mcp_app__plan_update {\"evidence\":\"No app_security AddDirectoryGrants or process_run was used\"}"]);
            (await Checks.RunAsync(checks, workspace, workspace, [], facts, CancellationToken.None)).ShouldAllBe(result => result.Passed);
            facts = facts with { Calls = [.. facts.Calls!, "mcp_app__app_security {\"operation\":\"AddDirectoryGrants\"}"] };
            (await Checks.RunAsync(checks, workspace, workspace, [], facts, CancellationToken.None))[0].Passed.ShouldBeFalse();
        }
        finally { directory.Delete(); }
    }

    [Fact]
    public async Task ALaterSuccessfulProbeDoesNotValidateAFailedOriginalCommand()
    {
        var directory = Directory.CreateTempSubdirectory("cli-eval-pairs-");
        try
        {
            var now = DateTimeOffset.UtcNow;
            ChatMessageView[] messages =
            [
                new(Guid.NewGuid(), null, "Assistant", "", now, ToolCalls: [new ChatToolCall("original", "mcp_built_in__process_run_sandboxed", "run.cmd")]),
                new(Guid.NewGuid(), null, "Tool", "{\"exitCode\":1}", now, ToolCallId: "original"),
                new(Guid.NewGuid(), null, "Assistant", "", now, ToolCalls: [new ChatToolCall("probe", "mcp_built_in__process_run_sandboxed", "node --version")]),
                new(Guid.NewGuid(), null, "Tool", "{\"exitCode\":0}", now, ToolCallId: "probe")
            ];
            var facts = new AttemptFacts(null, 2, 0, 0, directory.FullName, ToolTranscript: AttemptRunner.Transcript(messages));
            Check[] checks = [new(CheckType.ToolTranscriptMatches, Pattern: "CALL \\S*__process_run_sandboxed [^\\n]*run\\.cmd[^\\n]*\\nRESULT [^\\n]*\"exitCode\":0")];
            var workspace = Workspace.Snapshot(directory.FullName);
            (await Checks.RunAsync(checks, workspace, workspace, [], facts, CancellationToken.None))[0].Passed.ShouldBeFalse();
            messages[1] = messages[1] with { Content = "{\"exitCode\":0}" };
            facts = facts with { ToolTranscript = AttemptRunner.Transcript(messages) };
            (await Checks.RunAsync(checks, workspace, workspace, [], facts, CancellationToken.None))[0].Passed.ShouldBeTrue();
        }
        finally { directory.Delete(); }
    }
}
