namespace AI.Application.Tests.Tools;

using AI.Contracts.Tools;
using Shouldly;
using Xunit;

public class ToolPresentationsTests
{
    // The adapter set, codec and fallback the containers really produce.
    private readonly ToolsComposition _tools = new();

    private ToolCallResult Result(string json) => _tools.Codec.Read(json);

    [Theory]
    [InlineData("mcp_built_in__something_new", "Something new")]
    // Same tool name, but another server: the built-in adapters must not claim it.
    [InlineData("mcp_default__process_run", "Process run")]
    [InlineData("weather-lookup", "Weather lookup")]
    // '#' cannot appear in a protocol tool name, so the C# server has to write 'cs_run'; a person
    // still reads it as "C# run".
    [InlineData("mcp_csharp__cs_run", "C# run")]
    [InlineData("cs_run", "C# run")]
    // Only the abbreviation is expanded: any other name keeps its own spelling.
    [InlineData("mcp_built_in__css_lint", "Css lint")]
    [InlineData("mcp_built_in__csharp_analyze", "Csharp analyze")]
    [InlineData("", "Tool")]
    public void ShouldLabelAnyToolFromItsOwnName(string callName, string expected)
    {
        // Reformatting the server's own name is the whole liberty taken here. Nothing about what
        // the tool does is inferred, because nothing about it is known.
        _tools.Presentations.DescribeCall(callName, "{}").Title.ShouldBe(expected);
    }

    [Fact]
    public void ShouldSplitAServerPrefixOffTheToolName()
    {
        var tool = ToolRef.Parse("mcp_built_in__edit_file");

        tool.ServerPrefix.ShouldBe("mcp_built_in__");
        tool.Name.ShouldBe("edit_file");
        tool.IsBuiltIn.ShouldBeTrue();
    }

    [Fact]
    public void ShouldTreatAnUnprefixedNameAsTheWholeToolName()
    {
        var tool = ToolRef.Parse("search");

        tool.ServerPrefix.ShouldBeEmpty();
        tool.Name.ShouldBe("search");
        tool.IsBuiltIn.ShouldBeFalse();
    }

    [Fact]
    public void ShouldPutARecognizableArgumentOnTheRow()
    {
        var call = _tools.Presentations.DescribeCall(
            "some__tool", """{"depth":3,"path":"C:\\src\\MessageFeed.razor"}""");

        call.Detail.ShouldBe("C:\\src\\MessageFeed.razor");
    }

    [Fact]
    public void ShouldCountAListArgumentRatherThanInventingASummary()
    {
        var call = _tools.Presentations.DescribeCall("some__tool", """{"paths":["a.cs","b.cs","c.cs"]}""");

        call.Detail.ShouldBe("3 items");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"path":null}""")]
    public void ShouldLeaveTheDetailEmptyWhenArgumentsSayNothingUsable(string? arguments)
    {
        var call = _tools.Presentations.DescribeCall("some__tool", arguments);

        call.Title.ShouldBe("Tool");
        call.Detail.ShouldBeNull();
    }

    [Fact]
    public void ShouldReportAFailedResultAsAnError()
    {
        var described = _tools.Presentations.DescribeResult(
            "some__tool", "{}", Result("""{"isError":true,"error":"No directory grant covers this path."}"""));

        described.Severity.ShouldBe(ToolResultSeverity.Error);
        described.Summary.ShouldBe("No directory grant covers this path.");
    }

    [Fact]
    public void ShouldLiftScalarFieldsOfAnUnknownStructuredResultIntoFacts()
    {
        var described = _tools.Presentations.DescribeResult("some__tool", "{}",
            Result("""{"structuredContent":{"exitCode":0,"timedOut":false,"entries":[1,2],"nested":{"a":1}}}"""));

        described.Facts.Select(fact => fact.Label).ShouldBe(["Exit code", "Timed out"]);
        described.Facts[0].Value.ShouldBe("0");
        // Arrays and objects belong in the raw body, not in a list that has to stay scannable.
        described.Facts.Select(fact => fact.Label).ShouldNotContain("Entries");
    }

    [Fact]
    public void ShouldKeepTheCollapsedSummaryToASingleShortLine()
    {
        var long_ = new string('x', 400);
        var described = _tools.Presentations.DescribeResult("some__tool", "{}",
            Result($$"""{"content":[{"type":"text","text":"line one\nline two {{long_}}"}]}"""));

        described.Summary.ShouldNotContain("\n");
        described.Summary.Length.ShouldBeLessThanOrEqualTo(121);
        // The full text stays available for the expanded body.
        described.Body.ShouldNotBeNull();
        described.Body!.ShouldContain("line two");
    }

    [Fact]
    public void ShouldSayDoneRatherThanGuessWhenAResultCarriesNoText()
    {
        var described = _tools.Presentations.DescribeResult("some__tool", "{}", Result("""{"content":[]}"""));

        described.Summary.ShouldBe("Done");
        described.Severity.ShouldBe(ToolResultSeverity.Ok);
    }

    [Fact]
    public void ShouldFallBackToTheGenericAdapterWhenASpecificOneThrows()
    {
        var presentations = new ToolPresentations(new GenericToolPresentationAdapter(), [new ThrowingAdapter()]);

        var call = presentations.DescribeCall("some__tool", "{}");
        var result = presentations.DescribeResult("some__tool", "{}", Result("""{"content":[]}"""));

        call.Title.ShouldBe("Tool");
        result.Summary.ShouldBe("Done");
    }

    private sealed class ThrowingAdapter : IToolPresentationAdapter
    {
        public bool CanHandle(ToolRef tool) => true;
        public ToolCallPresentation DescribeCall(ToolRef tool, System.Text.Json.JsonElement? arguments) =>
            throw new InvalidOperationException("boom");
        public ToolResultPresentation DescribeResult(ToolRef tool, System.Text.Json.JsonElement? arguments, ToolCallResult result) =>
            throw new InvalidOperationException("boom");
    }
}
