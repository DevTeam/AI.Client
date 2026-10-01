using System.Text.Json;
using ModelContextProtocol.Client;
using Shouldly;
using Xunit;

namespace AI.Mcp.CSharp.Tests;

/// <summary>
/// Drives the real server over stdio, exactly as the Host does: child process, protocol handshake,
/// tool list and calls. This is what proves the tool works, not just that it compiles.
/// </summary>
public sealed class ScriptRunToolTests : IAsyncLifetime
{
    private static readonly string[] TwoArguments = ["hello", "world"];
    private static readonly string[] XmlImports = ["System.Xml.Linq"];
    private static readonly string[] XmlReferences = ["System.Xml.Linq"];
    private static readonly string[] MissingReference = ["No.Such.Assembly.Here"];

    private McpClient _client = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var assembly = Path.Combine(AppContext.BaseDirectory, "AI.Mcp.CSharp.dll");
        File.Exists(assembly).ShouldBeTrue($"The server assembly must be next to the tests: {assembly}");
        _client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "C# scripts",
            // Started through the muxer rather than the apphost so the test does not depend on
            // which runtime the test host resolved.
            Command = "dotnet",
            Arguments = [assembly],
            ShutdownTimeout = TimeSpan.FromMilliseconds(500)
        }), cancellationToken: Token);
    }

    public async ValueTask DisposeAsync() => await _client.DisposeAsync();

    [Fact]
    public async Task ListsTheScriptToolWithItsSchemas()
    {
        var tool = (await _client.ListToolsAsync(cancellationToken: Token)).ShouldHaveSingleItem();

        tool.Name.ShouldBe("csx_run");
        // The Host validates every result against the declared output schema, and shows the input
        // schema to the model, so both have to survive the round trip.
        tool.ProtocolTool.OutputSchema.ShouldNotBeNull();
        tool.ProtocolTool.InputSchema.GetProperty("properties").TryGetProperty("code", out _).ShouldBeTrue();
        tool.Description.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RunsAScriptAndReturnsEverythingItProduced()
    {
        var result = await CallAsync(new
        {
            code = """
                using System.Text;
                var greeting = new StringBuilder().Append(Args[0]).Append(' ').Append(Args[1]).ToString();
                var n = Globals["n"].GetInt32();
                Console.WriteLine(greeting + " n=" + n);
                greeting.Length + n
                """,
            arguments = TwoArguments,
            globals = new Dictionary<string, JsonElement> { ["n"] = JsonSerializer.SerializeToElement(41) }
        });

        result.GetProperty("success").GetBoolean().ShouldBeTrue();
        result.GetProperty("returnValue").GetString().ShouldBe("52");
        result.GetProperty("returnType").GetString().ShouldBe("System.Int32");
        // Console.WriteLine appends the platform newline, which the captured stream keeps as is.
        result.GetProperty("stdout").GetString().ShouldBe($"hello world n=41{Environment.NewLine}");
        result.GetProperty("stderr").GetString().ShouldBeEmpty();
        result.GetProperty("timedOut").GetBoolean().ShouldBeFalse();
        result.GetProperty("truncated").GetBoolean().ShouldBeFalse();
        result.GetProperty("error").ValueKind.ShouldBe(JsonValueKind.Null);

        var variables = result.GetProperty("variables").EnumerateArray()
            .ToDictionary(item => item.GetProperty("name").GetString()!, item => item);
        variables["greeting"].GetProperty("value").GetString().ShouldBe("hello world");
        variables["greeting"].GetProperty("type").GetString().ShouldBe("System.String");
    }

    [Fact]
    public async Task SetsTheWorkingDirectoryForTheRunAndRestoresIt()
    {
        var directory = Directory.CreateDirectory(
            Path.Combine(AppContext.BaseDirectory, "csx-" + Guid.NewGuid().ToString("N")));
        try
        {
            var result = await CallAsync(new
            {
                code = "Directory.GetCurrentDirectory()",
                workingDirectory = directory.FullName
            });

            result.GetProperty("success").GetBoolean().ShouldBeTrue();
            // The current directory is process-wide, so the run has to set it and put it back.
            var observed = result.GetProperty("returnValue").GetString();
            string.Equals(observed, Path.GetFullPath(directory.FullName), StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
            Directory.GetCurrentDirectory().ShouldNotBe(directory.FullName);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ReportsCompilationErrorsWithoutFailingTheCall()
    {
        var result = await CallAsync(new { code = "var x = ;" });

        result.GetProperty("success").GetBoolean().ShouldBeFalse();
        result.GetProperty("error").GetString().ShouldNotBeNullOrWhiteSpace();
        var diagnostic = result.GetProperty("diagnostics").EnumerateArray().First();
        diagnostic.GetProperty("severity").GetString().ShouldBe("Error");
        diagnostic.GetProperty("id").GetString().ShouldStartWith("CS");
        diagnostic.GetProperty("line").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ReportsAnExplicitlyThrownScriptException()
    {
        var result = await CallAsync(new { code = "throw new InvalidOperationException(\"boom\");" });

        result.GetProperty("success").GetBoolean().ShouldBeFalse();
        result.GetProperty("error").GetString().ShouldBe("InvalidOperationException: boom");
    }

    [Fact]
    public async Task CapturesStandardErrorSeparately()
    {
        var result = await CallAsync(new { code = "Console.Error.WriteLine(\"careful\"); Console.WriteLine(\"fine\"); 1" });

        result.GetProperty("success").GetBoolean().ShouldBeTrue();
        result.GetProperty("stdout").GetString().ShouldBe($"fine{Environment.NewLine}");
        result.GetProperty("stderr").GetString().ShouldBe($"careful{Environment.NewLine}");
    }

    [Fact]
    public async Task ReportsTimeoutInsteadOfHanging()
    {
        var result = await CallAsync(new { code = "System.Threading.Thread.Sleep(10000); 1", timeoutMs = 500 });

        result.GetProperty("timedOut").GetBoolean().ShouldBeTrue();
        result.GetProperty("success").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task TruncatesRunawayOutputInsteadOfExhaustingMemory()
    {
        var result = await CallAsync(new { code = "for (var i = 0; i < 200000; i++) Console.WriteLine(new string('x', 100)); 0" });

        result.GetProperty("success").GetBoolean().ShouldBeTrue();
        result.GetProperty("truncated").GetBoolean().ShouldBeTrue();
        result.GetProperty("stdout").GetString()!.Length.ShouldBe(32768);
    }

    [Fact]
    public async Task SkipsAnUnresolvableReferenceWithAWarning()
    {
        var result = await CallAsync(new { code = "1", references = MissingReference });

        result.GetProperty("success").GetBoolean().ShouldBeTrue();
        result.GetProperty("diagnostics").EnumerateArray()
            .ShouldContain(item => item.GetProperty("id").GetString() == "CSX0003");
    }

    [Fact]
    public async Task AddsTheRequestedImportsAndReferences()
    {
        var result = await CallAsync(new
        {
            code = "XDocument.Parse(\"<a><b/></a>\").Root!.Name.LocalName",
            imports = XmlImports,
            references = XmlReferences
        });

        result.GetProperty("success").GetBoolean().ShouldBeTrue();
        result.GetProperty("returnValue").GetString().ShouldBe("a");
    }

    [Fact]
    public async Task RejectsARelativeWorkingDirectory()
    {
        var result = await CallAsync(new { code = "1", workingDirectory = "relative/path" });

        result.GetProperty("success").GetBoolean().ShouldBeFalse();
        result.GetProperty("error").GetString().ShouldNotBeNull().ShouldContain("absolute");
    }

    [Fact]
    public async Task RejectsEmptyCode()
    {
        var result = await CallAsync(new { code = "   " });

        result.GetProperty("success").GetBoolean().ShouldBeFalse();
        result.GetProperty("error").GetString().ShouldNotBeNull().ShouldContain("empty");
    }

    private async Task<JsonElement> CallAsync(object arguments)
    {
        var result = await _client.CallToolAsync("csx_run",
            JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(arguments))!,
            cancellationToken: Token);

        result.StructuredContent.ShouldNotBeNull();
        result.StructuredContent!.Value.ValueKind.ShouldBe(JsonValueKind.Object);
        // A client without structured content support shows this text to the model instead.
        result.Content.ShouldNotBeEmpty();
        return result.StructuredContent.Value;
    }
}
