using AI.Client.Infrastructure.Chat;
using Shouldly;
using System.Text;
using Xunit;

namespace AI.Client.Infrastructure.Tests.Chat;

public class ChatCompletionSseParserTests
{
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

        chunks.ShouldBe(["Complete"]);
    }
}
