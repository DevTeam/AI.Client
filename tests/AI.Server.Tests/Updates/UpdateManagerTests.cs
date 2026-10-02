namespace AI.Server.Tests.Updates;

#pragma warning disable CA1859 // Exercise the service through its public interface.

using AI.Contracts.Updates;
using AI.Updates;
using Moq;
using Shouldly;
using System.Net;
using System.Security.Cryptography;
using Xunit;

public sealed class UpdateManagerTests
{
    [Theory]
    [InlineData("Desktop", false)]
    [InlineData("Host", true)]
    public async Task ProductDefaultsAndPreferencesSurviveRestart(string product, bool install)
    {
        await using var fixture = new Fixture();
        await using (var manager = fixture.Create(product))
        {
            manager.State.Preferences.InstallAutomatically.ShouldBe(install);
            manager.State.Preferences.Channel.ShouldBe(UpdateChannel.Preview);
            var preferences = new UpdatePreferences(false, false, false, UpdateChannel.Stable);
            await manager.ExecuteAsync("preferences", preferences, TestContext.Current.CancellationToken);
        }
        await using var restored = fixture.Create(product);
        restored.State.Preferences.ShouldBe(new UpdatePreferences(false, false, false, UpdateChannel.Stable));
    }

    [Fact]
    public async Task DigestMismatchNeverReachesInstaller()
    {
        await using var fixture = new Fixture();
        fixture.Release = fixture.Release with { Sha256 = new string('0', 64) };
        await using var manager = fixture.Create("Desktop");
        var token = TestContext.Current.CancellationToken;
        await manager.ExecuteAsync("check", null, token);
        var state = await manager.ExecuteAsync("install", null, token);
        state.Phase.ShouldBe(UpdatePhase.Failed);
        fixture.Installer.VerifyNoOtherCalls();
        fixture.Shutdown.ShouldBeFalse();
    }

    [Fact]
    public async Task SwitchingToStableDiscardsPreparedPreview()
    {
        await using var fixture = new Fixture();
        await using var manager = fixture.Create("Desktop");
        var token = TestContext.Current.CancellationToken;
        await manager.ExecuteAsync("check", null, token);
        await manager.ExecuteAsync("download", null, token);
        manager.State.Phase.ShouldBe(UpdatePhase.Ready);
        await manager.ExecuteAsync("preferences", manager.State.Preferences with { Channel = UpdateChannel.Stable }, token);
        manager.State.Release.ShouldBeNull();
        manager.State.Phase.ShouldBe(UpdatePhase.Idle);
        fixture.Installer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AutomaticHostUpdateWaitsForTasksWithoutLaunchingInstaller()
    {
        await using var fixture = new Fixture();
        await using var manager = fixture.Create("Host");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var worker = manager.RunAsync(stop.Token);
        await fixture.MaintenanceAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10), stop.Token);
        while (manager.State.Phase != UpdatePhase.WaitingForTasks) await Task.Delay(10, stop.Token);
        manager.State.Phase.ShouldBe(UpdatePhase.WaitingForTasks);
        fixture.Installer.VerifyNoOtherCalls();
        fixture.Shutdown.ShouldBeFalse();
        await stop.CancelAsync();
        await worker;
    }

    [Fact]
    public async Task FailedHelperLaunchReleasesMaintenanceAndDoesNotStopApplication()
    {
        await using var fixture = new Fixture { Idle = true };
        fixture.Installer.Setup(installer => installer.LaunchAsync(It.IsAny<UpdateState>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("launch failed"));
        await using var manager = fixture.Create("Host");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var worker = manager.RunAsync(stop.Token);
        await fixture.MaintenanceReleased.Task.WaitAsync(TimeSpan.FromSeconds(15), stop.Token);
        while (manager.State.Phase != UpdatePhase.Failed) await Task.Delay(10, stop.Token);
        manager.State.Phase.ShouldBe(UpdatePhase.Failed);
        fixture.Shutdown.ShouldBeFalse();
        await stop.CancelAsync();
        await worker;
    }

    [Fact]
    public async Task CanceledInstallationDoesNotAutomaticallyRetryOnRestart()
    {
        await using var fixture = new Fixture { Idle = true };
        await fixture.WriteFailedResultAsync();
        await using var manager = fixture.Create("Host");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var worker = manager.RunAsync(stop.Token);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (manager.State.Phase != UpdatePhase.Failed) await Task.Delay(10, timeout.Token);
        manager.State.FailedVersion.ShouldBe(fixture.Release.Version);
        fixture.Installer.VerifyNoOtherCalls();
        fixture.Shutdown.ShouldBeFalse();
        await stop.CancelAsync();
        await worker;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "updates-test-" + Guid.NewGuid().ToString("N"));
        private readonly HttpClient _http = new(new PackageHandler());
        public Mock<IUpdateInstaller> Installer { get; } = new(MockBehavior.Strict);
        public UpdateRelease Release { get; set; } = new("2.0.0-rc.1", true,
            "https://github.com/DevTeam/AI.Client/releases/tag/v2.0.0-rc.1", "https://example.invalid/package", "AI.Desktop-win-x64.exe",
            Convert.ToHexString(SHA256.HashData([1, 2, 3])), 3);
        public bool Idle { get; set; }
        public bool Shutdown { get; private set; }
        public TaskCompletionSource MaintenanceAttempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource MaintenanceReleased { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WriteFailedResultAsync()
        {
            var path = Path.Combine(_directory, "updates", "Host");
            Directory.CreateDirectory(path);
            await File.WriteAllTextAsync(Path.Combine(path, "result.json"),
                System.Text.Json.JsonSerializer.Serialize(new UpdateManager.UpdateResult(false, Release.Version)));
        }

        public IUpdateManager Create(string product)
        {
            var feed = new Mock<IUpdateFeed>();
            feed.Setup(item => item.FindAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<UpdateChannel>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => (Release, true));
            return new UpdateManager(product, _directory, _http, feed.Object, Installer.Object,
                new InstalledUpdateProduct("1.0.0", "win-x64", true), _ =>
                {
                    MaintenanceAttempted.TrySetResult();
                    return Task.FromResult(Idle);
                }, () => MaintenanceReleased.TrySetResult(), () => Shutdown = true);
        }

        public ValueTask DisposeAsync()
        {
            _http.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PackageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
    }
}
