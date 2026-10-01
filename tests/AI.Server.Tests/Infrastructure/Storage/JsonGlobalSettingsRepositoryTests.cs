using AI.Infrastructure.Storage;
using Moq;

namespace AI.Infrastructure.Tests.Storage;

using AI.Infrastructure.Settings;
using Shouldly;
using Xunit;

public sealed class JsonGlobalSettingsRepositoryTests
{
    [Fact]
    public async Task ShouldDropLegacyCostRatingWhenSavingWithoutLosingPrices()
    {
        var fileSystem = new MemoryFileSystem();
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(i => i.RootDirectory).Returns("data");
        var paths = new GlobalSettingsPaths(location.Object);
        fileSystem.Files[paths.SettingsPath] = """
            {
              "Connections": [{
                "Id": "11111111-1111-1111-1111-111111111111",
                "Name": "Legacy",
                "BaseUrl": "https://example.test/v1",
                "Model": "model",
                "Enabled": true,
                "IsDefault": true,
                "HasCredential": false,
                "Capability": 4,
                "Cost": 2,
                "Prices": { "Input": 1.5, "Output": 6, "CachedInput": 0.3 }
              }],
              "McpServers": [],
              "ToolPolicies": []
            }
            """;
        using var repository = new JsonGlobalSettingsRepository(fileSystem, paths);

        var settings = await repository.LoadAsync(CancellationToken.None);
        var connection = settings.Connections.ShouldHaveSingleItem();
        connection.Capability.ShouldBe(4);
        connection.Prices.ShouldBe(new AI.Contracts.Usage.TokenPrices(1.5m, 6m, 0.3m));

        await repository.SaveAsync(settings, CancellationToken.None);

        fileSystem.Files[paths.SettingsPath].ShouldNotContain("\"Cost\"");
        var reloaded = await repository.LoadAsync(CancellationToken.None);
        reloaded.Connections.ShouldHaveSingleItem().ShouldBe(connection);
    }

    [Fact]
    public async Task ShouldLoadConnectionWrittenBeforeContextLimitOverridesExisted()
    {
        var fileSystem = new MemoryFileSystem();
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(i => i.RootDirectory).Returns("data");
        var paths = new GlobalSettingsPaths(location.Object);
        fileSystem.Files[paths.SettingsPath] = """
            {
              "Connections": [{
                "Id": "11111111-1111-1111-1111-111111111111",
                "Name": "Legacy",
                "BaseUrl": "https://example.test/v1",
                "Model": "model",
                "Enabled": true,
                "IsDefault": true,
                "HasCredential": false
              }],
              "McpServers": [],
              "ToolPolicies": []
            }
            """;
        using var repository = new JsonGlobalSettingsRepository(fileSystem, paths);

        var settings = await repository.LoadAsync(CancellationToken.None);

        var connection = settings.Connections.ShouldHaveSingleItem();
        connection.ContextWindowTokens.ShouldBeNull();
        connection.ReservedOutputTokens.ShouldBeNull();
    }
}
