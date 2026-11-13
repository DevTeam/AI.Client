namespace AI.Server.Tests.Hosting;

using System.Net.Http.Headers;
using AI.Application.Settings;
using AI.Application.Users;
using AI.Contracts.Settings;
using AI.Domain.Users;
using AI.Server.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public sealed class LocalModelLifecycleLiveTests
{
    public static bool Enabled => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AI_LOCAL_MODEL_PACKAGE_LIVE_TEST"));

    [Fact(Skip = "Set AI_LOCAL_MODEL_PACKAGE_LIVE_TEST to a prepared package.", SkipUnless = nameof(Enabled))]
    public async Task DisablingUnloadsAndEnablingRestartsTheActualRuntime()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var token = timeout.Token;
        GlobalSettings state = new([], [], []);
        string? key = null;
        var settings = new Mock<IGlobalSettingsService>(MockBehavior.Strict);
        settings.Setup(item => item.GetAsync(It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(Volatile.Read(ref state)));
        settings.Setup(item => item.UpsertConnectionAsync(It.IsAny<ConnectionSettings>(),
                It.IsAny<ConnectionSettings?>(), It.IsAny<CancellationToken>()))
            .Returns((ConnectionSettings connection, ConnectionSettings? _, CancellationToken _) =>
            {
                var updated = Volatile.Read(ref state) with { Connections = [connection] };
                Volatile.Write(ref state, updated);
                return Task.FromResult(updated);
            });
        settings.Setup(item => item.SetConnectionCredentialAsync(LocalModelService.ConnectionId,
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, string? value, CancellationToken _) =>
            {
                Volatile.Write(ref key, value);
                return Task.FromResult(true);
            });
        var users = new Mock<IUserDirectory>();
        users.Setup(item => item.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync((UserId[])[UserId.Local]);
        using var service = new LocalModelService(new LocalModelOptions(
                Environment.GetEnvironmentVariable("AI_LOCAL_MODEL_PACKAGE_LIVE_TEST")!),
            settings.Object, users.Object, Mock.Of<IUserScope>(), NullLogger<LocalModelService>.Instance, new LocalModelPackageLocator(new AI.Contracts.FileSystem.SystemFileSystem(), new AI.Contracts.FileSystem.SystemPath()));
        await service.StartAsync(token);
        try
        {
            await WaitForStateAsync(service, "ready", token);
            var firstKey = Volatile.Read(ref key);
            Assert.False(string.IsNullOrEmpty(firstKey));
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstKey);
            var url = Assert.Single(Volatile.Read(ref state).Connections).BaseUrl + "/models";
            using (var response = await http.GetAsync(url, token)) response.EnsureSuccessStatusCode();

            var connection = Assert.Single(Volatile.Read(ref state).Connections);
            Volatile.Write(ref state, Volatile.Read(ref state) with { Connections = [connection with { Enabled = false }] });
            await WaitForStateAsync(service, "disabled", token);
            await Assert.ThrowsAsync<HttpRequestException>(async () => await http.GetAsync(url, token));

            Volatile.Write(ref state, Volatile.Read(ref state) with { Connections = [connection with { Enabled = true }] });
            await WaitForStateAsync(service, "ready", token);
            Assert.NotEqual(firstKey, Volatile.Read(ref key));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Volatile.Read(ref key));
            // The port may change when another program took it in the meantime.
            url = Assert.Single(Volatile.Read(ref state).Connections).BaseUrl + "/models";
            using (var response = await http.GetAsync(url, token)) response.EnsureSuccessStatusCode();
        }
        finally { await service.StopAsync(token); }
        Assert.Equal("stopped", service.Status.State);
    }

    private static async Task WaitForStateAsync(LocalModelService service, string expected, CancellationToken token)
    {
        while (service.Status.State != expected)
        {
            Assert.NotEqual("failed", service.Status.State);
            await Task.Delay(100, token);
        }
    }
}
