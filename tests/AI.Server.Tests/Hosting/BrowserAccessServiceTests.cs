namespace AI.Server.Tests.Hosting;

using AI.Application.Projects;
using AI.Infrastructure.Storage;
using AI.Server.Hosting;
using Shouldly;
using Xunit;

[Trait("Category", "Integration")]
public sealed class BrowserAccessServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ai-browser-access-" + Guid.NewGuid().ToString("N"));
    private readonly ManualClock _clock = new();

    [Fact]
    public void GrantSurvivesRestartWithoutSavingPlaintextAndCanBeRevoked()
    {
        var first = NewService();
        var token = first.Grant();

        token.Length.ShouldBe(64);
        File.ReadAllText(Path.Combine(_directory, "browser-access.txt")).ShouldNotContain(token);
        var restarted = NewService();
        restarted.Allows(token).ShouldBeTrue();
        restarted.Allows(new string('0', 64)).ShouldBeFalse();

        restarted.Revoke(token).ShouldBeTrue();
        NewService().Allows(token).ShouldBeFalse();
    }

    [Fact]
    public void PairingCodeGrantsAccessOnce()
    {
        var service = NewService();
        var code = service.CreatePairingCode();

        var token = service.RedeemPairingCode(code);

        token.ShouldNotBeNull();
        service.Allows(token).ShouldBeTrue();
        service.RedeemPairingCode(code).ShouldBeNull();
    }

    [Fact]
    public void PairingCodeExpires()
    {
        var service = NewService();
        var code = service.CreatePairingCode();

        _clock.UtcNow += TimeSpan.FromMinutes(3);

        service.RedeemPairingCode(code).ShouldBeNull();
    }

    [Fact]
    public void PairingCodeDoesNotSurviveRestart()
    {
        var code = NewService().CreatePairingCode();

        NewService().RedeemPairingCode(code).ShouldBeNull();
    }

    private BrowserAccessService NewService() =>
        new(new ProjectStorageLocation(new ServerOptions(_directory, null, true)), _clock);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class ManualClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    }
}
