namespace AI.Application.Tests.Settings;

using AI.Application.Projects;
using AI.Application.Settings;
using AI.Contracts.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

public class ConnectionContextWindowDetectorTests
{
    private static readonly ConnectionSettings Connection = new(
        Guid.CreateVersion7(), "Connection", "https://llm.example/v1", "model", true, true, false);

    private readonly Mock<IConnectionModelsResolver> _models = new(MockBehavior.Strict);
    private readonly Mock<IGlobalSecretStore> _secrets = new();
    private readonly Mock<IClock> _clock = new();
    private readonly MemoryRepository _repository = new(new GlobalSettings([Connection], [], []));
    private DateTimeOffset _now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    public ConnectionContextWindowDetectorTests()
    {
        _clock.SetupGet(item => item.UtcNow).Returns(() => _now);
        _secrets.Setup(item => item.GetAsync("connection", Connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync("secret");
    }

    [Fact]
    public async Task ShouldUseAndStoreTheWindowTheCatalogReportsForTheConnectionsModel()
    {
        Catalog(new ResolvedModelInfo("model", ContextWindowTokens: 131_072), new ResolvedModelInfo("other"));

        var detected = await CreateInstance().DetectAsync(Connection, CancellationToken.None);

        detected.DetectedContextWindow.ShouldBe(new DetectedContextWindow("model", 131_072));
        _repository.State.Connections.ShouldHaveSingleItem().DetectedContextWindow
            .ShouldBe(new DetectedContextWindow("model", 131_072));
        _models.Verify(item => item.ResolveAsync("https://llm.example/v1", "secret", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ShouldAskTheCatalogOncePerConnectionAndModel()
    {
        Catalog(new ResolvedModelInfo("model", ContextWindowTokens: 131_072),
            new ResolvedModelInfo("branch-model", ContextWindowTokens: 65_536));
        var detector = CreateInstance();

        (await detector.DetectAsync(Connection with { Model = "branch-model" }, CancellationToken.None))
            .DetectedContextWindow.ShouldBe(new DetectedContextWindow("branch-model", 65_536));
        (await detector.DetectAsync(Connection with { Model = "branch-model" }, CancellationToken.None))
            .DetectedContextWindow!.Tokens.ShouldBe(65_536);
        (await detector.DetectAsync(Connection, CancellationToken.None))
            .DetectedContextWindow!.Tokens.ShouldBe(131_072);

        _models.Verify(item => item.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        // Only the connection's own model is stored; a branch's choice is not the connection's.
        _repository.State.Connections.ShouldHaveSingleItem().DetectedContextWindow!.Model.ShouldBe("model");
    }

    [Fact]
    public async Task ShouldLeaveTheConnectionAloneWhenTheCatalogReportsNothing()
    {
        Catalog(new ResolvedModelInfo("model"));
        var detector = CreateInstance();

        (await detector.DetectAsync(Connection, CancellationToken.None)).ShouldBe(Connection);
        (await detector.DetectAsync(Connection, CancellationToken.None)).ShouldBe(Connection);

        _models.Verify(item => item.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _repository.Writes.ShouldBe(0);

        // A miss is asked again once it is old enough to be worth it.
        _now += TimeSpan.FromHours(1);
        await detector.DetectAsync(Connection, CancellationToken.None);
        _models.Verify(item => item.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ShouldFallBackWhenTheEndpointCannotBeReached()
    {
        _models.Setup(item => item.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Could not reach the endpoint."));

        var detected = await CreateInstance().DetectAsync(Connection, CancellationToken.None);

        detected.ShouldBe(Connection);
        new ConnectionContextLimitsResolver().Resolve(detected).ContextWindowTokens.ShouldBe(32_768);
    }

    [Fact]
    public async Task ShouldNotAskWhenTheWindowIsSetByHandOrAlreadyKnown()
    {
        var manual = Connection with { ContextWindowTokens = 8_192 };
        var known = Connection with { DetectedContextWindow = new DetectedContextWindow("model", 131_072) };
        var detector = CreateInstance();

        (await detector.DetectAsync(manual, CancellationToken.None)).ShouldBe(manual);
        (await detector.DetectAsync(known, CancellationToken.None)).ShouldBe(known);
    }

    private void Catalog(params ResolvedModelInfo[] models) =>
        _models.Setup(item => item.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(models);

    private ConnectionContextWindowDetector CreateInstance() => new(
        _models.Object, _secrets.Object, _repository, _clock.Object, NullLogger<ConnectionContextWindowDetector>.Instance);

    private sealed class MemoryRepository(GlobalSettings state) : IGlobalSettingsRepository
    {
        public GlobalSettings State { get; private set; } = state;
        public int Writes { get; private set; }

        public Task<GlobalSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(State);

        public Task SaveAsync(GlobalSettings settings, CancellationToken cancellationToken)
        {
            State = settings;
            Writes++;
            return Task.CompletedTask;
        }

        public async Task<GlobalSettings> UpdateAsync(
            Func<GlobalSettings, CancellationToken, Task<GlobalSettings>> update, CancellationToken cancellationToken)
        {
            State = await update(State, cancellationToken);
            Writes++;
            return State;
        }
    }
}
