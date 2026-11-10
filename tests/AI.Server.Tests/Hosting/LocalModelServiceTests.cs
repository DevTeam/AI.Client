namespace AI.Server.Tests.Hosting;

using System.Text.Json;
using AI.Application.Settings;
using AI.Application.Users;
using AI.Domain.Users;
using AI.Server.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

public sealed class LocalModelServiceTests
{
    [Fact]
    public async Task MissingPackageDisablesOnlyItsConnectionAndClearsItsCredential()
    {
        var connection = new AI.Contracts.Settings.ConnectionSettings(LocalModelService.ConnectionId,
            "Local Qwen3 0.6B", "http://127.0.0.1:52176/v1", "qwen3-0.6b-local", true, false, true);
        var disabled = connection with { Enabled = false };
        var settings = new Mock<IGlobalSettingsService>(MockBehavior.Strict);
        settings.Setup(item => item.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AI.Contracts.Settings.GlobalSettings([connection], [], []));
        settings.Setup(item => item.UpsertConnectionAsync(disabled, connection,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AI.Contracts.Settings.GlobalSettings([disabled], [], []));
        settings.Setup(item => item.SetConnectionCredentialAsync(connection.Id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var users = new Mock<IUserDirectory>();
        users.Setup(item => item.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync((UserId[])[UserId.Local]);
        using var service = new LocalModelService(new LocalModelOptions(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
            settings.Object, users.Object, Mock.Of<IUserScope>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalModelService>.Instance, new LocalModelPackageLocator(new AI.Contracts.FileSystem.SystemFileSystem(), new AI.Contracts.FileSystem.SystemPath()));
        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.ExecuteTask!;
        Assert.Equal("notInstalled", service.Status.State);
        settings.VerifyAll();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrCorruptedPackageDoesNotChangeConnections(bool corrupted)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ai-local-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            if (corrupted)
            {
                // A non-executable sentinel must never be launched when verification fails.
                await File.WriteAllTextAsync(Path.Combine(directory, "engine"), "not an executable", TestContext.Current.CancellationToken);
                await File.WriteAllTextAsync(Path.Combine(directory, "model.gguf"), "corrupt weights", TestContext.Current.CancellationToken);
                await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"),
                    JsonSerializer.Serialize(new LocalModelManifest("engine", "model.gguf", new string('0', 64))), TestContext.Current.CancellationToken);
            }
            var settings = new Mock<IGlobalSettingsService>(MockBehavior.Strict);
            var users = new Mock<IUserDirectory>(MockBehavior.Strict);
            users.Setup(item => item.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<UserId>());
            var logger = new Mock<ILogger<LocalModelService>>();
            logger.Setup(item => item.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            using var service = new LocalModelService(new LocalModelOptions(directory), settings.Object,
                users.Object, Mock.Of<IUserScope>(), logger.Object, new LocalModelPackageLocator(new AI.Contracts.FileSystem.SystemFileSystem(), new AI.Contracts.FileSystem.SystemPath()));
            await service.StartAsync(TestContext.Current.CancellationToken);
            await service.ExecuteTask!;
            settings.VerifyNoOtherCalls();
            users.Verify(item => item.ListAsync(It.IsAny<CancellationToken>()), corrupted ? Times.Never() : Times.Once());
            users.VerifyNoOtherCalls();
            Assert.Equal(corrupted ? "failed" : "notInstalled", service.Status.State);
            Assert.Equal(corrupted ? 1 : 0, logger.Invocations.Count(invocation => invocation.Method.Name == "Log"));
            await service.StopAsync(TestContext.Current.CancellationToken);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
