namespace AI.Infrastructure.Tests.Tools;

using AI.Application.Settings;
using AI.Application.Tools;
using AI.Contracts.Settings;
using AI.Contracts.Tools;
using AI.Infrastructure.Tools;
using Moq;
using Shouldly;
using Xunit;

public sealed class DefaultToolSessionFactoryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task ShouldLeaveTheSandboxOffUnlessTheSettingIsOn(bool? enabled)
    {
        var environment = await Factory(enabled is null ? null : new ToolSandboxSettings(enabled.Value))
            .CreateEnvironmentAsync([], ToolRunContext.None, TestContext.Current.CancellationToken);

        environment.ContainsKey(DefaultToolSessionFactory.SandboxVariable).ShouldBeFalse();
        environment.ContainsKey(DefaultToolSessionFactory.DirectoryGrantsVariable).ShouldBeTrue();
    }

    [Fact]
    public async Task ShouldSwitchTheSandboxOnWhenTheSettingIsOn()
    {
        var environment = await Factory(new ToolSandboxSettings(Enabled: true))
            .CreateEnvironmentAsync([], ToolRunContext.None, TestContext.Current.CancellationToken);

        DefaultToolSessionFactory.SandboxVariable.ShouldBe("AI_CLIENT_SANDBOX");
        environment[DefaultToolSessionFactory.SandboxVariable].ShouldBe("1");
    }

    private static DefaultToolSessionFactory Factory(ToolSandboxSettings? sandbox)
    {
        var settings = new Mock<IGlobalSettingsRepository>();
        settings.Setup(item => item.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalSettings([], [], [], null, sandbox));
        return new DefaultToolSessionFactory(Mock.Of<IToolResultModelProjector>(),
            Mock.Of<IChatTemporaryDirectory>(), settings.Object);
    }
}
