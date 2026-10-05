namespace AI.Application.Tests.Tools;

using AI.Contracts.Tools;
using Shouldly;
using Xunit;

public class CSharpToolPresentationTests
{
    // The adapter set, codec and fallback the containers really produce.
    private readonly ToolsComposition _tools = new();

    private ToolCallPresentation Call(string arguments) =>
        _tools.Presentations.DescribeCall("mcp_csharp__cs_run", arguments);

    private ToolResultPresentation Result(string structured) =>
        _tools.Presentations.DescribeResult("mcp_csharp__cs_run", "{}",
            _tools.Codec.Read($$"""{"structuredContent":{{structured}}}"""));

    [Fact]
    public void ShouldNameTheCallAfterWhatItDoesNotTheProtocolName()
    {
        // '#' cannot appear in a tool name, so the server writes 'cs_run'; the row still says C#.
        Call("""{"code":"1 + 1"}""").Title.ShouldBe("Run C#");
    }

    [Fact]
    public void ShouldShowTheFirstLineThatSaysSomethingAboutTheScript()
    {
        // A script normally opens with `using` directives; the row reads better below them.
        Call("""{"code":"using System;\n\nConsole.WriteLine(1);"}""").Detail.ShouldBe("Console.WriteLine(1);");
        Call("""{"code":"var total = 2;"}""").Detail.ShouldBe("var total = 2;");
    }

    [Fact]
    public void ShouldKeepALongLineOutOfTheRow()
    {
        var code = "var value = " + new string('1', 200) + ";";

        var detail = Call($$"""{"code":"{{code}}"}""").Detail;

        detail!.Length.ShouldBeLessThanOrEqualTo(72);
        detail.ShouldEndWith("…");
    }

    [Fact]
    public void ShouldTreatAScriptAsDangerousAsACommand()
    {
        // The scripting server is not a sandbox: a script reaches whatever its own process can.
        Call("""{"code":"1"}""").Safety.ShouldBe(ToolSafety.Destructive);
    }

    [Fact]
    public void ShouldShowWhatTheScriptReturned()
    {
        Result("""{"success":true,"returnValue":"42","returnType":"System.Int32","variables":[],"stdout":"","stderr":"","diagnostics":[],"durationMs":12,"timedOut":false,"truncated":false,"error":null}""")
            .Summary.ShouldBe("Returned 42");
        Result("""{"success":true,"returnValue":null,"returnType":null,"variables":[],"stdout":"hi","stderr":"","diagnostics":[],"durationMs":5,"timedOut":false,"truncated":false,"error":null}""")
            .Summary.ShouldBe("Completed");
    }

    [Fact]
    public void ShouldCountTheVariablesTheScriptDeclaredAndKeepTheirValuesInFacts()
    {
        var described = Result("""{"success":true,"returnValue":"6","returnType":"System.Int32","variables":[{"name":"total","type":"System.Int32","value":"6"},{"name":"name","type":"string","value":"a"}],"stdout":"","stderr":"","diagnostics":[],"durationMs":9,"timedOut":false,"truncated":false,"error":null}""");

        described.Facts.ShouldContain(fact => fact.Label == "Variables" && fact.Value == "2");
        described.Facts.ShouldContain(fact => fact.Label == "Return type" && fact.Value == "System.Int32");
        described.Facts.ShouldContain(fact => fact.Label == "Duration" && fact.Value == "9 ms");
    }

    [Fact]
    public void ShouldOfferTheScriptsOutputAsTheBody()
    {
        var described = Result("""{"success":true,"returnValue":null,"returnType":null,"variables":[],"stdout":"one\ntwo","stderr":"boom","diagnostics":[],"durationMs":3,"timedOut":false,"truncated":false,"error":null}""");

        described.Body.ShouldNotBeNull();
        described.Body!.ShouldContain("one\ntwo");
        described.Body!.ShouldContain("stderr:\nboom");
    }

    [Fact]
    public void ShouldSayTimedOutWhenTheRunWasCutShort()
    {
        var described = Result("""{"success":false,"returnValue":null,"returnType":null,"variables":[],"stdout":"","stderr":"","diagnostics":[],"durationMs":600000,"timedOut":true,"truncated":false,"error":"Script timed out."}""");

        described.Summary.ShouldBe("Timed out");
        described.Severity.ShouldBe(ToolResultSeverity.Warning);
        described.Facts.ShouldContain(fact => fact.Label == "Timed out" && fact.Value == "yes");
    }

    [Fact]
    public void ShouldSurfaceCompilerWarningsWithoutCallingThemFailures()
    {
        var described = Result("""{"success":true,"returnValue":null,"returnType":null,"variables":[],"stdout":"","stderr":"","diagnostics":[{"severity":"Warning","id":"CS0219","message":"The variable 'x' is assigned but never used.","line":2,"column":1}],"durationMs":20,"timedOut":false,"truncated":false,"error":null}""");

        described.Severity.ShouldBe(ToolResultSeverity.Warning);
        described.Facts.ShouldContain(fact => fact.Label == "Warnings" && fact.Value == "1");
        described.Facts.ShouldContain(fact => fact.Label == "Diagnostic 1" && fact.Value!.Contains("CS0219"));
        described.Facts.ShouldContain(fact => fact.Value!.Contains("line 2"));
    }

    [Fact]
    public void ShouldReportACompilationFailureThroughTheSharedErrorField()
    {
        var described = Result("""{"success":false,"returnValue":null,"returnType":null,"variables":[],"stdout":"","stderr":"","diagnostics":[{"severity":"Error","id":"CS0103","message":"The name 'x' does not exist in the current context","line":1,"column":1}],"durationMs":30,"timedOut":false,"truncated":false,"error":"CS0103: The name 'x' does not exist in the current context"}""");

        described.Severity.ShouldBe(ToolResultSeverity.Error);
        described.Summary.ShouldBe("CS0103: The name 'x' does not exist in the current context");
    }

    [Fact]
    public void ShouldMarkTruncatedOutputAsAWarning()
    {
        Result("""{"success":true,"returnValue":null,"returnType":null,"variables":[],"stdout":"…","stderr":"","diagnostics":[],"durationMs":4,"timedOut":false,"truncated":true,"error":null}""")
            .Summary.ShouldBe("Completed · output truncated");
    }

    [Fact]
    public void ShouldLeaveScriptingToolsOfAnotherServerToTheGenericAdapter()
    {
        // A third-party server may well declare a tool called cs_run; its result shape is its own.
        _tools.Presentations.DescribeCall("mcp_other__cs_run", """{"code":"1"}""")
            .Safety.ShouldBe(ToolSafety.Unknown);
    }

    [Fact]
    public void ShouldSurviveAResultThatDoesNotMatchTheExpectedShape()
    {
        var described = Result("""{"unexpected":[1,2]}""");

        described.ShouldNotBeNull();
        described.Summary.ShouldNotBeNullOrWhiteSpace();
    }
}
