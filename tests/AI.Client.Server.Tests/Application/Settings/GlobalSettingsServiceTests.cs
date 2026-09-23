namespace AI.Client.Application.Tests.Settings;

using AI.Client.Application.Settings;
using AI.Client.Contracts.Settings;
using Moq;
using Shouldly;
using Xunit;

public class GlobalSettingsServiceTests
{
    private readonly Mock<IGlobalSettingsRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IGlobalSecretStore> _secretStore = new(MockBehavior.Strict);

    [Fact]
    public async Task ShouldSelectFirstEnabledConnectionAsDefault()
    {
        var firstId = Guid.CreateVersion7();
        var secondId = Guid.CreateVersion7();
        GlobalSettings? saved = null;
        _repository.Setup(item => item.SaveAsync(It.IsAny<GlobalSettings>(), CancellationToken.None))
            .Callback<GlobalSettings, CancellationToken>((settings, _) => saved = settings)
            .Returns(Task.CompletedTask);
        _repository.Setup(item => item.LoadAsync(CancellationToken.None))
            .ReturnsAsync(() => saved!);
        _secretStore.Setup(item => item.ExistsAsync("connection", It.IsAny<Guid>(), CancellationToken.None))
            .ReturnsAsync(false);

        var result = await CreateInstance().SaveAsync(
            new SaveGlobalSettingsRequest(
                [
                    new ConnectionSettings(firstId, "Disabled", "https://one/v1", "one", false, false, false),
                    new ConnectionSettings(secondId, "Enabled", "https://two/v1", "two", true, false, false)
                ],
                [],
                []),
            CancellationToken.None);

        result.Connections.Single(item => item.Id == secondId).IsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task ShouldRejectDuplicateConnectionNames()
    {
        var request = new SaveGlobalSettingsRequest(
            [
                new ConnectionSettings(Guid.CreateVersion7(), "Company", "https://one/v1", "one", true, true, false),
                new ConnectionSettings(Guid.CreateVersion7(), "company", "https://two/v1", "two", true, false, false)
            ],
            [],
            []);

        var action = () => CreateInstance().SaveAsync(request, CancellationToken.None);

        await action.ShouldThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ShouldLetSeveralConnectionsShareTheSubtaskMark()
    {
        // Unlike the default, this mark is a set: a fan-out of subtasks is dealt out over every
        // connection wearing it, which is the whole reason for marking more than one.
        var result = await SaveAsync(
            new ConnectionSettings(Guid.CreateVersion7(), "One", "https://one/v1", "one", true, true, false, ForSubtasks: true),
            new ConnectionSettings(Guid.CreateVersion7(), "Two", "https://two/v1", "two", true, false, false, ForSubtasks: true));

        result.Connections.Count(item => item.ForSubtasks).ShouldBe(2);
    }

    [Fact]
    public async Task ShouldNotSendSubtasksToAConnectionThatIsTurnedOff()
    {
        var offId = Guid.CreateVersion7();
        var result = await SaveAsync(
            new ConnectionSettings(offId, "Off", "https://one/v1", "one", false, false, false, ForSubtasks: true),
            new ConnectionSettings(Guid.CreateVersion7(), "On", "https://two/v1", "two", true, false, false));

        result.Connections.Single(item => item.Id == offId).ForSubtasks.ShouldBeFalse();
    }

    [Fact]
    public async Task ShouldKeepARatingCoarseAndAbsentWhenNobodyGaveOne()
    {
        var id = Guid.CreateVersion7();
        // Zero and nine are not judgements anyone made; storing either would have the model read a
        // rating where none exists.
        var result = await SaveAsync(new ConnectionSettings(id, "One", "https://one/v1", "one", true, true, false,
            Capability: 0, Cost: 9, GoodFor: "  vision  "));

        var connection = result.Connections.Single(item => item.Id == id);
        connection.Capability.ShouldBeNull();
        connection.Cost.ShouldBeNull();
        connection.GoodFor.ShouldBe("vision");
    }

    [Theory]
    [InlineData(1023L, null)]
    [InlineData(32768L, 32768L)]
    [InlineData(null, 40000L)]
    public async Task ShouldRejectInvalidContextLimitOverrides(long? contextWindow, long? reservedOutput)
    {
        var connection = new ConnectionSettings(Guid.CreateVersion7(), "One", "https://one/v1", "one",
            true, true, false, ContextWindowTokens: contextWindow, ReservedOutputTokens: reservedOutput);

        var action = () => CreateInstance().SaveAsync(
            new SaveGlobalSettingsRequest([connection], [], []), CancellationToken.None);

        await action.ShouldThrowAsync<ArgumentException>();
    }

    private async Task<GlobalSettings> SaveAsync(params ConnectionSettings[] connections)
    {
        GlobalSettings? saved = null;
        _repository.Setup(item => item.SaveAsync(It.IsAny<GlobalSettings>(), CancellationToken.None))
            .Callback<GlobalSettings, CancellationToken>((settings, _) => saved = settings)
            .Returns(Task.CompletedTask);
        _repository.Setup(item => item.LoadAsync(CancellationToken.None)).ReturnsAsync(() => saved!);
        _secretStore.Setup(item => item.ExistsAsync("connection", It.IsAny<Guid>(), CancellationToken.None))
            .ReturnsAsync(false);
        return await CreateInstance().SaveAsync(new SaveGlobalSettingsRequest(connections, [], []), CancellationToken.None);
    }

    private GlobalSettingsService CreateInstance() => new(
        _repository.Object, _secretStore.Object, new ConnectionContextLimitsResolver());
}
