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
            []);

        var action = () => CreateInstance().SaveAsync(request, CancellationToken.None);

        await action.ShouldThrowAsync<ArgumentException>();
    }

    private GlobalSettingsService CreateInstance() => new(_repository.Object, _secretStore.Object);
}
