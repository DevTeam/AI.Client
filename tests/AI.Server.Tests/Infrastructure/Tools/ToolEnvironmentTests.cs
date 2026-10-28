namespace AI.Infrastructure.Tests.Tools;

using AI.Application.Settings;
using AI.Contracts.FileSystem;
using AI.Contracts.Settings;
using AI.Contracts.Tools;
using AI.Infrastructure.Tools;
using ModelContextProtocol.Client;
using Moq;
using Shouldly;
using Xunit;

/// <summary>Tool server processes start only through the tool environment (ADR-014).</summary>
public sealed class ToolEnvironmentTests
{
    [Fact]
    public async Task AUserStdioServerIsLaunchedThroughTheEnvironment()
    {
        var environment = new RecordingEnvironment();
        var factory = new ExternalToolSessionFactory(
            new ExternalMcpConnector(Mock.Of<IGlobalSecretStore>(), new SystemFileSystem(), new SystemPath(), environment),
            new ToolResultModelProjector());
        var server = new McpServerSettings(Guid.NewGuid(), "Tools", "Stdio", true, "Ask", null, "tool-server",
            ["--flag"], null, [new McpEnvironmentVariableSettings("MODE", "fast", false, false)], false);

        await Should.ThrowAsync<LaunchRefusedException>(() => factory.OpenAsync(server, TestContext.Current.CancellationToken));

        var launch = environment.Launches.ShouldHaveSingleItem();
        launch.Name.ShouldBe("Tools");
        launch.Command.ShouldBe("tool-server");
        launch.Arguments.ShouldBe(["--flag"]);
        launch.Environment["MODE"].ShouldBe("fast");
    }

    [Fact]
    public async Task TheBuiltInServerIsLaunchedThroughTheEnvironmentWithoutTheHostsEnvironment()
    {
        var environment = new RecordingEnvironment();
        var factory = new DefaultToolSessionFactory(Mock.Of<IToolResultModelProjector>(),
            Mock.Of<AI.Application.Tools.IChatTemporaryDirectory>(), environment);

        await Should.ThrowAsync<LaunchRefusedException>(() => factory.OpenAsync([],
            new AI.Application.Tools.ToolRunContext(Guid.Empty, Guid.Empty, Guid.Empty, true),
            TestContext.Current.CancellationToken));

        var launch = environment.Launches.ShouldHaveSingleItem();
        launch.Command.ShouldEndWith("AI.Mcp.BuiltIn" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        launch.InheritEnvironment.ShouldBe(false);
        launch.Environment.ShouldContainKey(DefaultToolSessionFactory.DirectoryGrantsVariable);
    }

    private sealed class LaunchRefusedException : Exception;

    private sealed class RecordingEnvironment : IToolEnvironment
    {
        public List<ToolProcessLaunch> Launches { get; } = [];

        public IClientTransport Launch(ToolProcessLaunch launch)
        {
            Launches.Add(launch);
            throw new LaunchRefusedException();
        }
    }
}
