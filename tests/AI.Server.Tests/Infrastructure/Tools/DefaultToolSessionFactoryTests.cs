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
    [Fact]
    public async Task ShouldSandboxScriptsButNotCommandsByDefault()
    {
        var environment = await Factory(null)
            .CreateEnvironmentAsync([], ToolRunContext.None, TestContext.Current.CancellationToken);

        environment.ContainsKey(DefaultToolSessionFactory.SandboxVariable).ShouldBeFalse();
        environment[DefaultToolSessionFactory.ScriptSandboxVariable].ShouldBe("1");
        environment.ContainsKey(DefaultToolSessionFactory.DirectoryGrantsVariable).ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ShouldPassEachSandboxSwitchOnlyWhenItIsOn(bool commands, bool scripts)
    {
        var environment = await Factory(new ToolSandboxSettings(commands, scripts))
            .CreateEnvironmentAsync([], ToolRunContext.None, TestContext.Current.CancellationToken);

        DefaultToolSessionFactory.SandboxVariable.ShouldBe("AI_CLIENT_SANDBOX");
        DefaultToolSessionFactory.ScriptSandboxVariable.ShouldBe("AI_CLIENT_SCRIPT_SANDBOX");
        environment.ContainsKey(DefaultToolSessionFactory.SandboxVariable).ShouldBe(commands);
        environment.ContainsKey(DefaultToolSessionFactory.ScriptSandboxVariable).ShouldBe(scripts);
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
