namespace AI.Infrastructure.Tests.Chat;

using AI.Application.Chat;
using AI.Application.Usage;
using AI.Infrastructure.Projects;
using AI.Contracts.Usage;
using AI.Infrastructure.Chat;
using Moq;
using Moq.Protected;
using Shouldly;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

public class ChatCompletionUsageTests
{
    private readonly Mock<HttpMessageHandler> _handler = new(MockBehavior.Strict);
    private readonly ConnectionRateLimits _rateLimits = new();

    [Fact]
    public async Task ShouldReadUsageThatArrivesAfterTheFinishReason()
    {
        const string sse = """
            data: {"model":"m","choices":[{"delta":{"content":"Hi"}}]}
            data: {"model":"m","choices":[{"delta":{},"finish_reason":"stop"}]}
            data: {"model":"m","choices":[],"usage":{"prompt_tokens":120,"completion_tokens":8,"prompt_tokens_details":{"cached_tokens":100},"completion_tokens_details":{"reasoning_tokens":3}}}
            data: [DONE]
            """;

        var chunks = await ParseAsync(sse);

        chunks.Select(chunk => chunk.Content).ShouldBe(["Hi", "", ""]);
        chunks[1].FinishReason.ShouldBe("stop");
        chunks[^1].Usage.ShouldBe(new ChatCompletionUsage(new TokenCounts(120, 8, 100, 3)));
    }

    [Fact]
    public async Task ShouldReadUsageCarriedOnTheFinishingChunk()
    {
        const string sse = """
            data: {"choices":[{"delta":{"content":"Hi"}}]}
            data: {"choices":[{"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":10,"completion_tokens":2,"cost":0.0004}}
            """;

        var chunks = await ParseAsync(sse);

        chunks[^1].Usage.ShouldBe(new ChatCompletionUsage(new TokenCounts(10, 2), 0.0004m));
    }

    [Fact]
    public async Task ShouldEndWithoutUsageWhenTheEndpointReportsNone()
    {
        const string sse = """
            data: {"choices":[{"delta":{"content":"Hi"}}]}
            data: {"choices":[{"delta":{},"finish_reason":"stop"}]}
            data: [DONE]
            """;

        var chunks = await ParseAsync(sse);

        chunks.ShouldAllBe(chunk => chunk.Usage == null);
        chunks[^1].FinishReason.ShouldBe("stop");
    }

    [Theory]
    [InlineData("""{"usage":{"prompt_tokens":50,"completion_tokens":5,"prompt_cache_hit_tokens":40}}""", 50, 5, 40)]
    [InlineData("""{"usage":{"input_tokens":30,"output_tokens":4,"cache_read_input_tokens":20}}""", 30, 4, 20)]
    [InlineData("""{"usage":{"prompt_tokens":10,"completion_tokens":1,"prompt_tokens_details":{"cached_tokens":99}}}""", 10, 1, 10)]
    public void ShouldUnderstandProviderSpellings(string json, long input, long output, long cached)
    {
        using var document = JsonDocument.Parse(json);

        var usage = new ChatCompletionUsageReader().Read(document.RootElement);

        usage.ShouldNotBeNull();
        usage.Tokens.ShouldBe(new TokenCounts(input, output, cached));
    }

    [Theory]
    [InlineData("""{"usage":null}""")]
    [InlineData("""{"usage":{"prompt_tokens":0,"completion_tokens":0}}""")]
    [InlineData("""{"choices":[]}""")]
    public void ShouldNotTakeAnEmptyReportForUsage(string json)
    {
        using var document = JsonDocument.Parse(json);

        new ChatCompletionUsageReader().Read(document.RootElement).ShouldBeNull();
    }

    [Fact]
    public async Task ShouldAskAStreamToReportUsage()
    {
        string? body = null;
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
                body = request.Content!.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult())
            .ReturnsAsync(() => Response(HttpStatusCode.OK, "data: [DONE]\n\n"));

        await DrainAsync(CreateInstance(), "https://llm.example/v1");

        body.ShouldNotBeNull();
        body.ShouldContain("\"stream_options\":{\"include_usage\":true}");
    }

    [Fact]
    public async Task ShouldStreamWithoutUsageFromAnEndpointThatRefusesToReportIt()
    {
        var bodies = new List<string>();
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
                bodies.Add(request.Content!.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult()))
            .ReturnsAsync(() => bodies.Count == 1
                ? Response(HttpStatusCode.BadRequest, """{"error":"Unrecognized request argument supplied: stream_options"}""")
                : Response(HttpStatusCode.OK, "data: [DONE]\n\n"));
        var client = CreateInstance();

        await DrainAsync(client, "https://strict.example/v1");
        await DrainAsync(client, "https://strict.example/v1/");

        bodies.Count.ShouldBe(3);
        bodies[0].ShouldContain("stream_options");
        bodies[1].ShouldNotContain("stream_options");
        // Remembered: the next request does not pay for the refusal again.
        bodies[2].ShouldNotContain("stream_options");
    }

    [Fact]
    public async Task ShouldNotRetryARefusalThatIsNotAboutUsage()
    {
        var sent = 0;
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback(() => sent++)
            .ReturnsAsync(() => Response(HttpStatusCode.BadRequest, """{"error":"invalid tool_calls"}"""));

        await Should.ThrowAsync<ChatEndpointException>(() => DrainAsync(CreateInstance(), "https://llm.example/v1"));

        sent.ShouldBe(1);
    }

    [Fact]
    public async Task ShouldReturnUsageOfANonStreamedCompletion()
    {
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => Response(HttpStatusCode.OK,
                """{"model":"m","choices":[{"message":{"content":"Hello"}}],"usage":{"prompt_tokens":9,"completion_tokens":1}}"""));

        var response = await CreateInstance().CompleteAsync(
            new ChatCompletionRequest("https://llm.example/v1", "m", null, "Hi"), CancellationToken.None);

        response.Usage.ShouldBe(new ChatCompletionUsage(new TokenCounts(9, 1)));
    }

    private static async Task<List<ChatCompletionChunk>> ParseAsync(string sse)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sse));
        var chunks = new List<ChatCompletionChunk>();
        await foreach (var chunk in new ChatCompletionSseParser(new ChatCompletionUsageReader(), new ChatTransportPolicy()).ParseAsync(stream, CancellationToken.None))
            chunks.Add(chunk);
        return chunks;
    }

    private static async Task DrainAsync(OpenAiCompatibleChatCompletionClient client, string baseUrl)
    {
        await foreach (var _ in client.StreamAsync(new ChatCompletionRequest(baseUrl, "m", null, "Hi"), CancellationToken.None)) { }
    }

    private OpenAiCompatibleChatCompletionClient CreateInstance() =>
        new(new HttpClient(_handler.Object), new ChatCompletionSseParser(new ChatCompletionUsageReader(), new ChatTransportPolicy()),
            new ChatTransportPolicy(), new ChatCompletionUsageReader(), new RateLimitHeaderReader(), _rateLimits, new SystemClock());

    [Fact]
    public async Task ShouldKeepTheLimitsTheEndpointStatesForItsConnection()
    {
        var connection = Guid.NewGuid();
        var response = Response(HttpStatusCode.OK, "data: [DONE]\n\n");
        response.Headers.Add("x-ratelimit-limit-requests", "500");
        response.Headers.Add("x-ratelimit-remaining-requests", "499");
        response.Headers.Add("x-ratelimit-reset-requests", "120ms");
        response.Headers.Add("x-ratelimit-remaining-tokens", "39000");
        _handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        await foreach (var _ in CreateInstance().StreamAsync(
                           new ChatCompletionRequest("https://llm.example/v1", "m", null, "Hi", connection), CancellationToken.None)) { }

        var limits = _rateLimits.Find(connection).ShouldNotBeNull();
        limits.Requests.ShouldNotBeNull().Remaining.ShouldBe(499);
        limits.Requests.Limit.ShouldBe(500);
        limits.Tokens.ShouldNotBeNull().Remaining.ShouldBe(39_000);
        limits.Tokens.Limit.ShouldBeNull();
    }

    private static HttpResponseMessage Response(HttpStatusCode statusCode, string content) =>
        new(statusCode) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
}
