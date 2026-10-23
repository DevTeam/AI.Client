namespace AI.Infrastructure.Tests.Tools;

using System.Text.Json;
using AI.Application.Tools;
using AI.Contracts.Tools;
using AI.Infrastructure.Tools;
using Moq;
using Shouldly;
using Xunit;

public sealed class DefaultToolSessionFactoryTests
{
    /// <summary>
    /// Commands and scripts always run in the sandbox, which opens exactly these grants, so the grants
    /// are all the server is told: there is no switch to pass any more.
    /// </summary>
    [Fact]
    public void ShouldHandTheServerTheProjectGrantsAndTheChatTemporaryDirectory()
    {
        var temporary = new Mock<IChatTemporaryDirectory>();
        temporary.Setup(item => item.GetOrCreate(It.IsAny<Guid>(), It.IsAny<Guid>())).Returns(@"C:\temp\chat");
        var factory = new DefaultToolSessionFactory(Mock.Of<IToolResultModelProjector>(), temporary.Object, new LocalToolEnvironment());
        var run = ToolRunContext.None with { ProjectId = Guid.NewGuid(), ChatId = Guid.NewGuid() };

        var environment = factory.CreateEnvironment([new ToolDirectoryGrant(@"C:\work", true, ["read"])], run);

        var grants = JsonDocument.Parse(environment[DefaultToolSessionFactory.DirectoryGrantsVariable]!).RootElement
            .EnumerateArray().Select(grant => (Root: grant.GetProperty("root").GetString(), Purpose: grant.GetProperty("purpose").GetString()))
            .ToArray();
        grants.ShouldBe([(@"C:\work", "project"), (@"C:\temp\chat", "chatTemporary")]);
        environment.Keys.ShouldNotContain(key => key.Contains("SANDBOX", StringComparison.Ordinal));
    }
}
