namespace AI.Client.Application.Tests.Tools;

using AI.Client.Contracts.Tools;
using Shouldly;
using System.Text.Json;
using Xunit;

public sealed class AppToolPresentationTests
{
    [Fact]
    public void ShouldNameTheResourceBeingRead()
    {
        var call = ToolPresentations.Default.DescribeCall("mcp_app__app_read",
            """{"resource":"Messages","chatId":"0199c0de-0000-7000-8000-000000000001"}""");

        call.Title.ShouldBe("Read messages");
        call.Detail.ShouldBe("chat 0199c0de");
        call.Safety.ShouldBe(ToolSafety.ReadOnly);
    }

    [Fact]
    public void ShouldSummariseHowMuchOfAPageCameBack()
    {
        var result = ToolPresentations.Default.DescribeResult("mcp_app__app_read", """{"resource":"Chats"}""",
            Structured("""{"resource":"Chats","items":[],"nextCursor":"2","truncated":false,"returned":2,"total":9,"error":null}"""));

        result.Summary.ShouldBe("2 of 9 items");
        result.Severity.ShouldBe(ToolResultSeverity.Ok);
        result.Facts.ShouldContain(fact => fact.Label == "Next cursor" && fact.Value == "2");
    }

    [Fact]
    public void ShouldWarnWhenAPageEndedOnItsCharacterBudget()
    {
        var result = ToolPresentations.Default.DescribeResult("mcp_app__app_read", null,
            Structured("""{"resource":"Messages","items":[],"nextCursor":"1","truncated":true,"returned":1,"total":4,"error":null}"""));

        result.Severity.ShouldBe(ToolResultSeverity.Warning);
    }

    [Fact]
    public void ShouldDescribeAChangeByWhatItDid()
    {
        var result = ToolPresentations.Default.DescribeResult("mcp_app__app_chats", """{"operation":"Create"}""",
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
        var result = ToolPresentations.Default.DescribeResult("mcp_app__app_chats", """{"operation":"Rename"}""",
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
        ToolPresentations.Default.DescribeCall("mcp_app__app_runs", """{"operation":"Submit","content":"Go"}""")
            .Safety.ShouldBe(ToolSafety.Mutating);
        ToolPresentations.Default.DescribeCall("mcp_app__app_security", """{"operation":"SetProjectSecurity"}""")
            .Safety.ShouldBe(ToolSafety.Destructive);
    }

    [Fact]
    public void ShouldNotClaimAThirdPartyToolThatSharesAName()
    {
        // Tool names are unique only within a server, so the prefix is what decides ownership.
        ToolPresentations.Default.DescribeCall("mcp_other__app_read", """{"resource":"Projects"}""")
            .Title.ShouldBe("App read");
    }

    private static ToolCallResult Structured(string json, bool isError = false)
    {
        var content = JsonDocument.Parse(json).RootElement.Clone();
        return new ToolCallResult([new ToolContent(ToolContentKind.Text, content.GetRawText(), null, null, null)],
            content, null, isError, content.GetRawText());
    }
}
