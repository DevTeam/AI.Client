namespace AI.Client.Application.Tests.Tools;

using AI.Client.Contracts.Tools;
using Shouldly;
using System.Text.Json;
using Xunit;

public sealed class AppToolPresentationTests
{
    // The adapter set, codec and fallback the containers really produce.
    private readonly ToolsComposition _tools = new();

    [Fact]
    public void ShouldNameTheResourceBeingRead()
    {
        var call = _tools.Presentations.DescribeCall("mcp_app__app_read",
            """{"resource":"Messages","chatId":"0199c0de-0000-7000-8000-000000000001"}""");

        call.Title.ShouldBe("Read messages");
        call.Detail.ShouldBe("chat 0199c0de");
        call.Safety.ShouldBe(ToolSafety.ReadOnly);
    }

    [Fact]
    public void ShouldSummariseHowMuchOfAPageCameBack()
    {
        var result = _tools.Presentations.DescribeResult("mcp_app__app_read", """{"resource":"Chats"}""",
            Structured("""{"resource":"Chats","items":[],"nextCursor":"2","truncated":false,"returned":2,"total":9,"error":null}"""));

        result.Summary.ShouldBe("2 of 9 items");
        result.Severity.ShouldBe(ToolResultSeverity.Ok);
        result.Facts.ShouldContain(fact => fact.Label == "Next cursor" && fact.Value == "2");
    }

    [Fact]
    public void ShouldWarnWhenAPageEndedOnItsCharacterBudget()
    {
        var result = _tools.Presentations.DescribeResult("mcp_app__app_read", null,
            Structured("""{"resource":"Messages","items":[],"nextCursor":"1","truncated":true,"returned":1,"total":4,"error":null}"""));

        result.Severity.ShouldBe(ToolResultSeverity.Warning);
    }

    [Fact]
    public void ShouldDescribeAChangeByWhatItDid()
    {
        var result = _tools.Presentations.DescribeResult("mcp_app__app_chats", """{"operation":"Create"}""",
            Structured("""
                {"operation":"Create","applied":true,"dryRun":false,"effect":"Created chat 'Notes'.",
                 "projectId":"0199c0de-0000-7000-8000-000000000002","chatId":null,"branchId":null,"messageId":null,
                 "revision":1,"status":null,"current":null,"replayed":false,"error":null}
                """));

        result.Summary.ShouldBe("Created chat 'Notes'.");
        result.Severity.ShouldBe(ToolResultSeverity.Ok);
        result.Facts.ShouldContain(fact => fact.Label == "Revision" && fact.Value == "1");
    }

    [Fact]
    public void ShouldTreatAConflictAsSomethingToNoticeRatherThanAFailure()
    {
        var result = _tools.Presentations.DescribeResult("mcp_app__app_chats", """{"operation":"Rename"}""",
            Structured("""
                {"operation":"Rename","applied":false,"dryRun":false,"effect":"Nothing was changed.",
                 "projectId":null,"chatId":null,"branchId":null,"messageId":null,"revision":7,"status":"Conflict",
                 "current":null,"replayed":false,"error":"The revision does not match the stored one."}
                """, isError: true));

        result.Severity.ShouldBe(ToolResultSeverity.Warning);
        result.Facts.ShouldContain(fact => fact.Label == "Status" && fact.Value == "Conflict");
    }

    [Fact]
    public void ShouldMarkARunAsMutatingRatherThanDestructive()
    {
        _tools.Presentations.DescribeCall("mcp_app__app_runs", """{"operation":"Submit","content":"Go"}""")
            .Safety.ShouldBe(ToolSafety.Mutating);
        _tools.Presentations.DescribeCall("mcp_app__app_security", """{"operation":"SetProjectSecurity"}""")
            .Safety.ShouldBe(ToolSafety.Destructive);
    }

    [Fact]
    public void ShouldNotClaimAThirdPartyToolThatSharesAName()
    {
        // Tool names are unique only within a server, so the prefix is what decides ownership.
        _tools.Presentations.DescribeCall("mcp_other__app_read", """{"resource":"Projects"}""")
            .Title.ShouldBe("App read");
    }

    [Fact]
    public void ShouldSummariseDelegatedWorkAndKeepItsTranscriptOutOfTheSummary()
    {
        var structured = JsonDocument.Parse("""
            {"results":[
              {"task":"count the files","answer":"There are 12.","connection":"Cheap","messages":3,"toolCalls":2,"filesChanged":0,"completed":true,"error":null},
              {"task":"break","answer":"","connection":"Cheap","messages":1,"toolCalls":0,"filesChanged":0,"completed":false,"error":"The endpoint refused."}],
             "error":null}
            """).RootElement.Clone();
        var meta = JsonDocument.Parse("""
            {"transcript":[{"task":"count the files","role":"assistant","content":"Looking.","toolName":"mcp_built_in__list_directory"}]}
            """).RootElement.Clone();
        var result = new ToolCallResult([new ToolContent(ToolContentKind.Text, structured.GetRawText(), null, null, null)],
            structured, meta, false, structured.GetRawText());

        var presentation = _tools.Presentations.DescribeResult("mcp_app__spawn_subtask", """{"tasks":["count the files"]}""", result);

        presentation.Summary.ShouldBe("1 of 2 subtasks failed");
        presentation.Severity.ShouldBe(ToolResultSeverity.Warning);
        // A label is a label: the task prompts live in the arguments, not in the definition list.
        // The connection is part of the record: it says what the work was worth.
        presentation.Facts.ShouldContain(fact => fact.Label == "Subtask 1" && fact.Value.Contains("Cheap · 2 tool calls"));
        presentation.Facts.ShouldAllBe(fact => fact.Label.Length <= 12);
        // The transcript is what the expandable body is for; the collapsed row never carries it.
        presentation.Body.ShouldNotBeNull().ShouldContain("list_directory");
        presentation.Summary.ShouldNotContain("Looking");
    }

    [Fact]
    public void ShouldKeepASubtaskTranscriptThroughStorageAndOutOfTheModelProjection()
    {
        var structured = JsonDocument.Parse("""
            {"results":[{"task":"count","answer":"12","connection":"Test","messages":2,"toolCalls":1,"filesChanged":0,"completed":true,"error":null}],"error":null}
            """).RootElement.Clone();
        var meta = JsonDocument.Parse("""
            {"transcript":[{"task":"count","role":"assistant","content":"secret reasoning","toolName":null}]}
            """).RootElement.Clone();
        var live = new ToolCallResult([new ToolContent(ToolContentKind.Text, structured.GetRawText(), null, null, null)],
            structured, meta, false, _tools.ModelProjector.Project(
                [new ToolContent(ToolContentKind.Text, structured.GetRawText(), null, null, null)], structured, false));

        // History is what the card is rendered from, so the transcript has to survive the round trip
        // — and the model projection has to be re-derived without it on the way back.
        var restored = _tools.Codec.Read(_tools.Codec.Write(live));

        restored.ModelContent.ShouldNotContain("secret reasoning");
        _tools.Presentations.DescribeResult("mcp_app__spawn_subtask", """{"tasks":["count"]}""", restored)
            .Body.ShouldNotBeNull().ShouldContain("secret reasoning");
    }

    [Fact]
    public void ShouldDescribeADelegationByItsFirstTask()
    {
        var call = _tools.Presentations.DescribeCall("mcp_app__spawn_subtask",
            """{"tasks":[{"task":"read the changelog"},{"task":"summarise it"}]}""");

        call.Title.ShouldBe("Delegate 2 subtasks");
        call.Detail.ShouldBe("read the changelog");

        // History written before tasks carried their own connection still describes itself.
        _tools.Presentations.DescribeCall("mcp_app__spawn_subtask", """{"tasks":["older shape"]}""")
            .Detail.ShouldBe("older shape");
    }

    private static ToolCallResult Structured(string json, bool isError = false)
    {
        var content = JsonDocument.Parse(json).RootElement.Clone();
        return new ToolCallResult([new ToolContent(ToolContentKind.Text, content.GetRawText(), null, null, null)],
            content, null, isError, content.GetRawText());
    }
}
