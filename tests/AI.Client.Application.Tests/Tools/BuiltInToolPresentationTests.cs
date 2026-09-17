namespace AI.Client.Application.Tests.Tools;

using AI.Client.Contracts.Tools;
using Shouldly;
using Xunit;

public class BuiltInToolPresentationTests
{
    private static ToolCallPresentation Call(string name, string arguments) =>
        Shipped.DescribeCall("mcp_built_in__" + name, arguments);

    private static ToolResultPresentation Result(string name, string arguments, string structured) =>
        Shipped.DescribeResult("mcp_built_in__" + name, arguments,
            ToolResultCodec.Read($$"""{"structuredContent":{{structured}}}"""));

    [Fact]
    public void ShouldNameFileCallsAfterWhatTheyDoToWhichFile()
    {
        Call("read_text_file", """{"path":"C:\\src\\web\\MessageFeed.razor"}""").Title.ShouldBe("Read file");
        // Only the tail of the path: every row in one directory shares the prefix.
        Call("read_text_file", """{"path":"C:\\src\\web\\MessageFeed.razor"}""").Detail.ShouldBe("web/MessageFeed.razor");
        Call("edit_file", """{"path":"/a/b/c.cs"}""").Title.ShouldBe("Edit file");
        Call("search_files", """{"path":"/src","pattern":"*.razor"}""").Detail.ShouldBe("*.razor");
        // A content search is named after what is being looked for, not where.
        Call("grep_files", """{"path":"/src","query":"IToolSession"}""").Title.ShouldBe("Search in files");
        Call("grep_files", """{"path":"/src","query":"IToolSession"}""").Detail.ShouldBe("IToolSession");
    }

    [Fact]
    public void ShouldCountFilesRatherThanNamingThemForABatchRead()
    {
        Call("read_multiple_files", """{"paths":["a.cs","b.cs"]}""").Detail.ShouldBe("2 files");
        Call("read_multiple_files", """{"paths":["a.cs"]}""").Detail.ShouldBe("1 file");
    }

    [Fact]
    public void ShouldMarkWritingToolsDestructiveAndReadingToolsReadOnly()
    {
        Call("read_text_file", """{"path":"/a"}""").Safety.ShouldBe(ToolSafety.ReadOnly);
        Call("edit_file", """{"path":"/a"}""").Safety.ShouldBe(ToolSafety.Destructive);
        Call("write_file", """{"path":"/a"}""").Safety.ShouldBe(ToolSafety.Destructive);
        Call("create_directory", """{"path":"/a"}""").Safety.ShouldBe(ToolSafety.Mutating);
        Call("delete_file", """{"path":"/a"}""").Safety.ShouldBe(ToolSafety.Destructive);
        Call("delete_directory", """{"path":"/a"}""").Safety.ShouldBe(ToolSafety.Destructive);
    }

    [Fact]
    public void ShouldSayWhenADirectoryDeleteTakesTheWholeSubtreeWithIt()
    {
        // The destructive flag is on the tool either way; the row should tell the two calls apart.
        Call("delete_directory", """{"path":"/src/gen","recursive":true}""").Title.ShouldBe("Delete directory (recursive)");
        Call("delete_directory", """{"path":"/src/gen","recursive":false}""").Title.ShouldBe("Delete directory");
        Call("delete_directory", """{"path":"/src/gen"}""").Title.ShouldBe("Delete directory");
        Call("delete_directory", """{"path":"/src/gen"}""").Detail.ShouldBe("src/gen");
    }

    [Fact]
    public void ShouldNotCallADryRunEditDestructive()
    {
        // The server declares the tool destructive; this invocation asked it not to write.
        Call("edit_file", """{"path":"/a","dryRun":true}""").Safety.ShouldBe(ToolSafety.ReadOnly);
    }

    [Fact]
    public void ShouldSummarizeResultsFromWhatTheToolActuallyReported()
    {
        Result("read_text_file", "{}", """{"path":"/a.cs","lineCount":42,"truncated":false}""")
            .Summary.ShouldBe("42 lines");
        Result("search_files", "{}", """{"path":"/src","matches":["a","b","c"],"truncated":false}""")
            .Summary.ShouldBe("3 matches");
        Result("list_directory", "{}", """{"path":"/src","entries":[],"truncated":false}""")
            .Summary.ShouldBe("0 entries");
        // Matching lines are the answer; the file count is how widely they are spread.
        Result("grep_files", "{}",
                """{"path":"/src","query":"x","files":[{"path":"/src/a.cs"}],"totalMatches":7,"filesScanned":3,"filesSkipped":0,"truncated":false}""")
            .Summary.ShouldBe("7 matches in 1 file");
        Result("edit_file", "{}", """{"path":"/a.cs","applied":2,"diff":"-x\n+y","dryRun":false}""")
            .Summary.ShouldBe("2 edits");
        Result("create_directory", "{}", """{"path":"/a","created":false}""")
            .Summary.ShouldBe("Already existed");
        // A byte count below 1 KB keeps this assertion independent of the machine's culture.
        Result("delete_file", "{}", """{"path":"/a.cs","deleted":true,"bytes":512}""")
            .Summary.ShouldBe("Deleted · 512 B");
        Result("delete_directory", "{}", """{"path":"/src","deleted":true,"recursive":false}""")
            .Summary.ShouldBe("Deleted");
        Result("delete_directory", "{}", """{"path":"/src","deleted":true,"recursive":true}""")
            .Summary.ShouldBe("Deleted, recursive");
    }

    [Fact]
    public void ShouldSurfaceTruncationAsAWarningWithoutCallingItAFailure()
    {
        var described = Result("list_directory", "{}", """{"path":"/src","entries":[1,2],"truncated":true}""");

        described.Severity.ShouldBe(ToolResultSeverity.Warning);
        described.Summary.ShouldBe("2 entries, truncated");
    }

    [Fact]
    public void ShouldOfferAnEditDiffAsADiffBody()
    {
        var described = Result("edit_file", "{}", """{"path":"/a.cs","applied":1,"diff":"-alpha\n+beta","dryRun":false}""");

        described.BodyFormat.ShouldBe(ToolBodyFormat.Diff);
        described.Body.ShouldBe("-alpha\n+beta");
    }

    [Fact]
    public void ShouldReportABuiltInToolsOwnErrorFieldAsTheFailure()
    {
        // Built-in tools report the reason in the result, not only through isError.
        var described = Result("read_text_file", "{}", """{"path":"/x","error":"No directory grant covers this path."}""");

        described.Severity.ShouldBe(ToolResultSeverity.Error);
        described.Summary.ShouldBe("No directory grant covers this path.");
    }

    [Fact]
    public void ShouldShowTheCommandForAProcessRun()
    {
        var call = Call("process_run", """{"executable":"dotnet","arguments":["build","--no-restore"]}""");

        call.Title.ShouldBe("Run command");
        call.Detail.ShouldBe("dotnet build --no-restore");
        call.Safety.ShouldBe(ToolSafety.Destructive);
    }

    [Fact]
    public void ShouldTreatANonZeroExitAsAWarningRatherThanAnError()
    {
        // The call did what it was asked; the command failed. That is a real outcome the model
        // will act on, not a host failure.
        var described = Result("process_run", "{}",
            """{"exitCode":1,"stdout":"","stderr":"boom","durationMs":12,"timedOut":false,"truncated":false}""");

        described.Severity.ShouldBe(ToolResultSeverity.Warning);
        described.Summary.ShouldBe("Exit 1");
        described.Facts.ShouldContain(fact => fact.Label == "Exit code" && fact.Value == "1");
        described.Body.ShouldNotBeNull();
        described.Body!.ShouldContain("stderr:");
    }

    [Fact]
    public void ShouldSayTimedOutWhenThatIsWhatHappened()
    {
        Result("process_run", "{}",
                """{"exitCode":null,"stdout":"","stderr":"","durationMs":600,"timedOut":true,"truncated":false}""")
            .Summary.ShouldBe("Timed out");
    }

    [Fact]
    public void ShouldShowTheHostForAFetchAndItsStatusForTheResult()
    {
        Call("fetch", """{"url":"https://example.com/a/b?c=d"}""").Detail.ShouldBe("example.com");

        var described = Result("fetch", "{}",
            """{"url":"https://example.com","status":404,"contentType":"text/html","content":"…","truncated":false}""");
        described.Summary.ShouldBe("HTTP 404");
        described.Severity.ShouldBe(ToolResultSeverity.Warning);
    }

    [Theory]
    [InlineData("read_text_file")]
    [InlineData("edit_file")]
    [InlineData("process_run")]
    [InlineData("fetch")]
    public void ShouldSurviveAResultThatDoesNotMatchTheExpectedShape(string name)
    {
        // Structured content is validated against the output schema on the way in, but a chat file
        // can be hand-edited and a schema can change between builds.
        var described = Shipped.DescribeResult(
            "mcp_built_in__" + name, "{}", ToolResultCodec.Read("""{"structuredContent":{"unexpected":[1,2]}}"""));

        described.ShouldNotBeNull();
        described.Summary.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ShouldLeaveToolsFromOtherServersToTheGenericAdapter()
    {
        // A third-party server may well have a tool called edit_file; its result shape is its own.
        Shipped.DescribeCall("mcp_other__edit_file", """{"path":"/a"}""")
            .Title.ShouldBe("Edit file");
        Shipped.DescribeCall("mcp_other__edit_file", """{"path":"/a"}""")
            .Safety.ShouldBe(ToolSafety.Unknown);
    }
}
