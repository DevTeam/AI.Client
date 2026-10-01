namespace AI.Infrastructure.Tests.Chat;

using AI.Application.Chat;
using AI.Infrastructure.Chat;
using Shouldly;
using System.Text;
using Xunit;

public class ChatCompletionSseParserTests
{
    [Fact]
    public async Task ShouldAssembleInterleavedToolArgumentsOnlyOnCompletion()
    {
        const string sse = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"one","function":{"name":"run","arguments":"{\"x\":"}},{"index":1,"id":"two","function":{"name":"read","arguments":"{}"}}]}}]}
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"1}"}}]}}]}
            data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sse));
        var chunks = new List<ChatCompletionChunk>();
        await foreach (var chunk in new ChatCompletionSseParser(new ChatCompletionUsageReader(), new ChatTransportPolicy()).ParseAsync(stream, CancellationToken.None)) chunks.Add(chunk);
        chunks.Count.ShouldBe(2);
        chunks[0].ToolCallsStarted.ShouldBeTrue();
        chunks[0].ToolCallName.ShouldBe("run");
        chunks[0].ToolCalls.ShouldBeNull();
        var calls = chunks[1].ToolCalls!;
        calls[0].Arguments.ShouldBe("{\"x\":1}");
        calls[1].Id.ShouldBe("two");
    }

    [Fact]
    public async Task ShouldAnnounceToolNameWhenItArrivesAfterTheFirstDelta()
    {
        const string sse = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"one"}]}}]}
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"name":"run","arguments":"{}"}}]}}]}
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":""}}]}}]}
            data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sse));
        var chunks = new List<ChatCompletionChunk>();
        await foreach (var chunk in new ChatCompletionSseParser(new ChatCompletionUsageReader(), new ChatTransportPolicy()).ParseAsync(stream, CancellationToken.None)) chunks.Add(chunk);
        chunks.Where(chunk => chunk.ToolCallsStarted).Select(chunk => chunk.ToolCallName).ShouldBe([null, "run"]);
        chunks[^1].ToolCalls!.Single().Name.ShouldBe("run");
    }

    [Fact]
    public async Task ShouldNotExecuteTruncatedToolCall()
    {
        const string sse = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"one","function":{"name":"run","arguments":"{}"}}]}}]}
            data: {"choices":[{"delta":{},"finish_reason":"length"}]}
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sse));
        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in new ChatCompletionSseParser(new ChatCompletionUsageReader(), new ChatTransportPolicy()).ParseAsync(stream, CancellationToken.None)) { }
        });
    }
    [Fact]
    public async Task ShouldReportThatAnAnswerWasCutOffAtTheTokenLimit()
    {
        const string sse = """
            data: {"choices":[{"delta":{"content":"Half a sen"}}]}
            data: {"choices":[{"delta":{},"finish_reason":"length"}]}
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sse));
        var chunks = new List<ChatCompletionChunk>();
        await foreach (var chunk in new ChatCompletionSseParser(new ChatCompletionUsageReader(), new ChatTransportPolicy()).ParseAsync(stream, CancellationToken.None)) chunks.Add(chunk);

        // The text, then the fact that there was meant to be more of it.
        chunks.Select(chunk => chunk.Content).ShouldBe(["Half a sen", ""]);
        chunks[^1].FinishReason.ShouldBe("length");
    }

    [Fact]
    public async Task ShouldReadContentDeltasUntilDone()
    {
        const string sse = """
            : keep-alive

            data: {"model":"test-model","choices":[{"delta":{"role":"assistant"}}]}

            data: {"model":"test-model","choices":[{"delta":{"content":"Hello"}}]}

            data: {"model":"test-model","choices":[{"delta":{"content":" world"}}]}

            data: [DONE]

            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sse));
        var chunks = new List<string>();

        await foreach (var chunk in new ChatCompletionSseParser(new ChatCompletionUsageReader(), new ChatTransportPolicy()).ParseAsync(stream, CancellationToken.None))
        {
            chunks.Add(chunk.Content);
            chunk.Model.ShouldBe("test-model");
        }

        chunks.ShouldBe(["Hello", " world"]);
    }

    [Fact]
    public async Task ShouldFinishWhenEndpointReportsFinishReasonWithoutDoneMarker()
    {
        const string sse = """
            data: {"model":"test-model","choices":[{"delta":{"content":"Complete"}}]}

            data: {"model":"test-model","choices":[{"delta":{},"finish_reason":"stop"}]}

            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sse));
        var chunks = new List<string>();

        await foreach (var chunk in new ChatCompletionSseParser(new ChatCompletionUsageReader(), new ChatTransportPolicy()).ParseAsync(stream, CancellationToken.None))
        {
            chunks.Add(chunk.Content);
        }

        // The trailing empty chunk is the one that carries the finish reason.
        chunks.ShouldBe(["Complete", ""]);
    }

    [Fact]
    public async Task ShouldKeepAStreamAliveWhileTheModelReasonsBetweenTextAndAToolCall()
    {
        var pipe = new System.IO.Pipelines.Pipe();
        var parser = new ChatCompletionSseParser(new ChatCompletionUsageReader(),
            new ChatTransportPolicy(streamIdleTimeout: TimeSpan.FromMilliseconds(300)));
        var writing = Task.Run(async () =>
        {
            await Write(pipe, """data: {"choices":[{"delta":{"content":"Creating the files now."}}]}""");
            // Thinking for well past the idle timeout, in deltas the parser does not show.
            for (var step = 0; step < 8; step++)
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
                await Write(pipe, """data: {"choices":[{"delta":{"reasoning_content":"..."}}]}""");
            }
            await Write(pipe, """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"one","function":{"name":"write_file","arguments":"{}"}}]}}]}""");
            await Write(pipe, """data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""");
            await pipe.Writer.CompleteAsync();
        }, TestContext.Current.CancellationToken);

        var chunks = new List<ChatCompletionChunk>();
        await foreach (var chunk in parser.ParseAsync(pipe.Reader.AsStream(), CancellationToken.None)) chunks.Add(chunk);
        await writing;

        chunks[^1].ToolCalls!.Single().Name.ShouldBe("write_file");
    }

    [Fact]
    public async Task ShouldNotTakeAStreamThatFellSilentForAFinishedAnswer()
    {
        var pipe = new System.IO.Pipelines.Pipe();
        var parser = new ChatCompletionSseParser(new ChatCompletionUsageReader(),
            new ChatTransportPolicy(streamIdleTimeout: TimeSpan.FromMilliseconds(100)));
        await Write(pipe, """data: {"choices":[{"delta":{"content":"Creating the files now."}}]}""");

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in parser.ParseAsync(pipe.Reader.AsStream(), CancellationToken.None)) { }
        });
        await pipe.Writer.CompleteAsync();
    }

    private static async Task Write(System.IO.Pipelines.Pipe pipe, string line) =>
        await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(line + "\n\n"), TestContext.Current.CancellationToken);
}
