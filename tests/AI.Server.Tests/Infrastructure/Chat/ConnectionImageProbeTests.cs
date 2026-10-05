namespace AI.Infrastructure.Tests.Chat;

using System.Net;
using System.Text;
using AI.Application.Settings;
using AI.Contracts.Settings;
using AI.Infrastructure.Chat;
using Moq;
using Moq.Protected;
using Shouldly;
using Xunit;

public sealed class ConnectionImageProbeTests
{
    [Fact]
    public async Task ShouldTestSelectedModelWithImageAndSavedCredential()
    {
        var id = Guid.NewGuid();
        var settings = new Mock<IGlobalSettingsRepository>();
        settings.Setup(item => item.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new GlobalSettings(
            [new ConnectionSettings(id, "Vision", "https://example.test/v1", "old-model", true, false, true)], [], []));
        var secrets = new Mock<IGlobalSecretStore>();
        secrets.Setup(item => item.GetAsync("connection", id, It.IsAny<CancellationToken>())).ReturnsAsync("saved-key");
        var handler = new Mock<HttpMessageHandler>();
        string? body = null;
        string? authorization = null;
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                body = request.Content!.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult();
                authorization = request.Headers.Authorization?.ToString();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{}", Encoding.UTF8, "application/json") });

        var result = await new ConnectionImageProbe(new HttpClient(handler.Object), settings.Object, secrets.Object)
            .TestAsync(id, new ImageProbeRequest("https://example.test/v1", "selected-model"), CancellationToken.None);

        result.Supported.ShouldBeTrue();
        body.ShouldNotBeNull().ShouldContain("selected-model");
        body.ShouldContain("data:image/png;base64,");
        authorization.ShouldBe("Bearer saved-key");
    }
}
