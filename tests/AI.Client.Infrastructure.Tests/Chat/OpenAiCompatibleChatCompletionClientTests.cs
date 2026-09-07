namespace AI.Client.Infrastructure.Tests.Chat;

using AI.Client.Contracts.Chat;
using AI.Client.Infrastructure.Chat;
using Moq;
using Moq.Protected;
using Shouldly;
using System.Net;
using System.Text;
using Xunit;

public class OpenAiCompatibleChatCompletionClientTests
{
    private readonly Mock<HttpMessageHandler> _handler = new(MockBehavior.Strict);

    [Fact]
    public async Task ShouldSendOpenAiCompatibleRequestWithBearerToken()
    {
        // Given
        var client = CreateInstance();
        HttpRequestMessage? capturedRequest = null;
        string? capturedContent = null;
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                capturedRequest = request;
                capturedContent = request.Content!.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult();
            })
            .ReturnsAsync(CreateResponse(HttpStatusCode.OK, """{"model":"test-model","choices":[{"message":{"content":"Hello"}}]}"""));

        // When
        var response = await client.CompleteAsync(
            new ChatCompletionRequest("https://llm.example/v1", "test-model", "secret", "Hi"),
            CancellationToken.None);

        // Then
        response.ShouldBe(new ChatCompletionResponse("Hello", "test-model"));
        capturedRequest.ShouldNotBeNull();
        capturedRequest.RequestUri.ShouldBe(new Uri("https://llm.example/v1/chat/completions"));
        capturedRequest.Headers.Authorization?.Scheme.ShouldBe("Bearer");
        capturedRequest.Headers.Authorization?.Parameter.ShouldBe("secret");
        capturedContent.ShouldNotBeNull();
        capturedContent.ShouldContain("\"model\":\"test-model\"");
        capturedContent.ShouldContain("\"stream\":false");
    }

    [Fact]
    public async Task ShouldNotSendAuthorizationWhenApiKeyIsEmpty()
    {
        // Given
        var client = CreateInstance();
        HttpRequestMessage? capturedRequest = null;
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(CreateResponse(HttpStatusCode.OK, """{"choices":[{"message":{"content":"Hello"}}]}"""));

        // When
        await client.CompleteAsync(
            new ChatCompletionRequest("http://llm.example/v1", "test-model", " ", "Hi"),
            CancellationToken.None);

        // Then
        capturedRequest.ShouldNotBeNull();
        capturedRequest.Headers.Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task ShouldSendSelectedBranchAsCompletionContext()
    {
        // Given
        var client = CreateInstance();
        string? capturedContent = null;
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
                capturedContent = request.Content!.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult())
            .ReturnsAsync(CreateResponse(HttpStatusCode.OK, """{"choices":[{"message":{"content":"Hello"}}]}"""));

        // When
        await client.CompleteAsync(new ChatCompletionRequest(
            "https://llm.example/v1",
            "test-model",
            null,
            "New question",
            ContextMessages: [
                new ChatCompletionMessage("user", "First question"),
                new ChatCompletionMessage("assistant", "First answer"),
                new ChatCompletionMessage("user", "New question")]), CancellationToken.None);

        // Then
        capturedContent.ShouldNotBeNull();
        capturedContent.ShouldContain("First question");
        capturedContent.ShouldContain("First answer");
        capturedContent.ShouldContain("New question");
    }

    [Fact]
    public async Task ShouldRequestAndReadStreamingChunks()
    {
        string? capturedContent = null;
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
                capturedContent = request.Content!.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult())
            .ReturnsAsync(CreateResponse(
                HttpStatusCode.OK,
                "data: {\"model\":\"test-model\",\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}\n\ndata: [DONE]\n\n"));
        var chunks = new List<string>();

        await foreach (var chunk in CreateInstance().StreamAsync(
                           new ChatCompletionRequest("https://llm.example/v1", "test-model", null, "Hi"),
                           CancellationToken.None))
        {
            chunks.Add(chunk.Content);
        }

        chunks.ShouldBe(["Hello"]);
        capturedContent.ShouldNotBeNull();
        capturedContent.ShouldContain("\"stream\":true");
    }

    [Fact]
    public async Task ShouldOmitToolsWhenProjectDoesNotExposeAny()
    {
        string? capturedContent = null;
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
                capturedContent = request.Content!.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult())
            .ReturnsAsync(CreateResponse(HttpStatusCode.OK, "data: [DONE]\n\n"));

        await foreach (var _ in CreateInstance().StreamAsync(
                           new ChatCompletionRequest("https://llm.example/v1", "test-model", null, "Hi", Tools: []),
                           CancellationToken.None)) { }

        capturedContent.ShouldNotBeNull();
        capturedContent.ShouldNotContain("\"tools\"");
    }

    private OpenAiCompatibleChatCompletionClient CreateInstance() =>
        new(new HttpClient(_handler.Object), new ChatCompletionSseParser());

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string content) =>
        new(statusCode) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
}
