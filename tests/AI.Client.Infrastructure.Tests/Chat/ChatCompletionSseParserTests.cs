namespace AI.Client.Infrastructure.Tests.Chat;

using AI.Client.Infrastructure.Chat;
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
        var chunks = new List<Contracts.Chat.ChatCompletionChunk>();
        await foreach (var chunk in new ChatCompletionSseParser().ParseAsync(stream, CancellationToken.None)) chunks.Add(chunk);
        var calls = chunks.ShouldHaveSingleItem().ToolCalls!;
        calls[0].Arguments.ShouldBe("{\"x\":1}");
        calls[1].Id.ShouldBe("two");
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
            await foreach (var _ in new ChatCompletionSseParser().ParseAsync(stream, CancellationToken.None)) { }
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
        var chunks = new List<Contracts.Chat.ChatCompletionChunk>();
        await foreach (var chunk in new ChatCompletionSseParser().ParseAsync(stream, CancellationToken.None)) chunks.Add(chunk);

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

        await foreach (var chunk in new ChatCompletionSseParser().ParseAsync(stream, CancellationToken.None))
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

        await foreach (var chunk in new ChatCompletionSseParser().ParseAsync(stream, CancellationToken.None))
        {
            chunks.Add(chunk.Content);
        }

        // The trailing empty chunk is the one that carries the finish reason.
        chunks.ShouldBe(["Complete", ""]);
    }
}
