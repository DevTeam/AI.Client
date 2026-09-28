namespace AI.Server.Tests.Hosting;

using AI.Infrastructure.Storage;
using AI.Server.Hosting;
using Shouldly;
using Xunit;

public sealed class BrowserAccessServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ai-browser-access-" + Guid.NewGuid().ToString("N"));

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

    private BrowserAccessService NewService() =>
        new(new ProjectStorageLocation(new ServerOptions(_directory, null, true)));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
