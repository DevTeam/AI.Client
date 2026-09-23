namespace AI.Client.Infrastructure.Tests.Settings;

using AI.Client.Infrastructure.Settings;
using Moq;
using Moq.Protected;
using Shouldly;
using System.Net;
using System.Text;
using Xunit;

public class OpenAiCompatibleConnectionModelsResolverTests
{
    private readonly Mock<HttpMessageHandler> _handler = new(MockBehavior.Strict);

    [Fact]
    public async Task ShouldReturnTheModelsArrayUnderData()
    {
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(Ok("""
                {"object":"list","data":[
                    {"id":"alpha","display_name":"Alpha","owned_by":"team-a"},
                    {"id":"beta","title":"Beta","owned_by":"team-b"}
                ]}
                """));

        var models = await CreateInstance().ResolveAsync("https://llm.example/v1", "secret", CancellationToken.None);

        models.Select(item => item.Id).ShouldBe(["alpha", "beta"]);
        models[0].DisplayName.ShouldBe("Alpha");
        models[1].DisplayName.ShouldBe("Beta");
        models[1].OwnedBy.ShouldBe("team-b");
    }

    [Fact]
    public async Task ShouldAcceptTheBareArrayShapeToo()
    {
        // Some OpenAI-compatible proxies omit the wrapping { "data": [...] } and return the array
        // directly; the resolver should not silently produce an empty list for that.
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(Ok("""[{"id":"solo"}]"""));

        var models = await CreateInstance().ResolveAsync("https://llm.example/v1", null, CancellationToken.None);

        models.Select(item => item.Id).ShouldBe(["solo"]);
    }

    [Fact]
    public async Task ShouldSortModelsAlphabeticallyForStableRendering()
    {
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(Ok("""
                {"data":[{"id":"charlie"},{"id":"alpha"},{"id":"bravo"}]}
                """));

        var models = await CreateInstance().ResolveAsync("https://llm.example/v1", null, CancellationToken.None);

        models.Select(item => item.Id).ShouldBe(["alpha", "bravo", "charlie"]);
    }

    [Fact]
    public async Task ShouldDeduplicateByIdRegardlessOfCase()
    {
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(Ok("""
                {"data":[{"id":"Alpha"},{"id":"alpha","owned_by":"other"},{"id":"ALPHA"}]}
                """));

        var models = await CreateInstance().ResolveAsync("https://llm.example/v1", null, CancellationToken.None);

        models.Count.ShouldBe(1);
        models[0].Id.ShouldBe("Alpha");
    }

    [Fact]
    public async Task ShouldSkipEntriesThatAreMissingAnId()
    {
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(Ok("""
                {"data":[{"display_name":"No id"},{"id":""},{"id":"good"}]}
                """));

        var models = await CreateInstance().ResolveAsync("https://llm.example/v1", null, CancellationToken.None);

        models.Select(item => item.Id).ShouldBe(["good"]);
    }

    [Fact]
    public async Task ShouldSendBearerTokenOnlyWhenAKeyIsProvided()
    {
        HttpRequestMessage? captured = null;
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(Ok("""{"data":[]}"""));

        await CreateInstance().ResolveAsync("https://llm.example/v1", "  secret  ", CancellationToken.None);

        captured.ShouldNotBeNull();
        captured.Headers.Authorization?.Scheme.ShouldBe("Bearer");
        captured.Headers.Authorization?.Parameter.ShouldBe("secret");
    }

    [Fact]
    public async Task ShouldPointAtTheModelsEndpointRegardlessOfTrailingSlash()
    {
        HttpRequestMessage? captured = null;
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(Ok("""{"data":[]}"""));

        await CreateInstance().ResolveAsync("https://llm.example/v1/", null, CancellationToken.None);

        captured.ShouldNotBeNull();
        captured.RequestUri.ShouldBe(new Uri("https://llm.example/v1/models"));
    }

    [Fact]
    public async Task ShouldRejectAnAbsoluteUrlThatIsNotHttpOrHttps()
    {
        var action = () => CreateInstance().ResolveAsync("ftp://llm.example/v1", null, CancellationToken.None);

        await action.ShouldThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ShouldSurfaceTheErrorBodyWhenTheEndpointRefuses()
    {
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"error":{"message":"Invalid API key"}}""", Encoding.UTF8, "application/json")
            });

        var error = await Should.ThrowAsync<InvalidOperationException>(() => CreateInstance()
            .ResolveAsync("https://llm.example/v1", "bad", CancellationToken.None));

        error.Message.ShouldContain("401");
        error.Message.ShouldContain("Invalid API key");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("llm.example/v1")]
    [InlineData("not a url")]
    public async Task ShouldRejectAnEmptyOrRelativeBaseUrlWithoutCallingTheNetwork(string baseUrl)
    {
        var action = () => CreateInstance().ResolveAsync(baseUrl, null, CancellationToken.None);

        await action.ShouldThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ShouldExplainAnUnreachableEndpoint()
    {
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("No such host is known."));

        var error = await Should.ThrowAsync<InvalidOperationException>(() => CreateInstance()
            .ResolveAsync("https://nowhere.example/v1", null, CancellationToken.None));

        error.Message.ShouldContain("https://nowhere.example/v1/models");
        error.Message.ShouldContain("No such host is known.");
    }

    [Fact]
    public async Task ShouldExplainAnEndpointThatAnswersWithAWebPage()
    {
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body>Welcome</body></html>", Encoding.UTF8, "text/html")
            });

        var error = await Should.ThrowAsync<InvalidOperationException>(() => CreateInstance()
            .ResolveAsync("https://llm.example", null, CancellationToken.None));

        error.Message.ShouldContain("did not return JSON");
    }

    [Fact]
    public async Task ShouldReduceAnHtmlErrorPageToItsTitle()
    {
        // A corporate proxy answers an unknown host with its own error page, styles and all.
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent(
                    "<!DOCTYPE html><html><head><title>ERROR: The requested URL could not be retrieved</title>"
                    + "<style>* { font-family: verdana; }</style></head><body>...</body></html>",
                    Encoding.UTF8, "text/html")
            });

        var error = await Should.ThrowAsync<InvalidOperationException>(() => CreateInstance()
            .ResolveAsync("https://nowhere.example/v1", null, CancellationToken.None));

        error.Message.ShouldBe("The endpoint returned 503 (Service Unavailable). ERROR: The requested URL could not be retrieved");
    }

    [Fact]
    public async Task ShouldReturnEmptyListWhenThePayloadIsEmpty()
    {
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(Ok(""));

        var models = await CreateInstance().ResolveAsync("https://llm.example/v1", null, CancellationToken.None);

        models.ShouldBeEmpty();
    }

    private OpenAiCompatibleConnectionModelsResolver CreateInstance() =>
        new(new HttpClient(_handler.Object) { Timeout = Timeout.InfiniteTimeSpan });

    private static HttpResponseMessage Ok(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
}
