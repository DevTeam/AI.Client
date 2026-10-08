namespace AI.Application.Tests.Tools;

using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Settings;
using AI.Application.Tools;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Settings;
using Moq;
using Shouldly;
using Xunit;

public sealed class ToolPolicyResolverTests
{
    [Fact]
    public async Task SavedRulesOverrideTheBuiltInDefaultAtEachScope()
    {
        var projectId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var serverId = DefaultMcpServer.Id;
        const string name = "read_text_file";
        const string schema = "current";
        var now = DateTimeOffset.UtcNow;
        var project = new ProjectDetails(projectId, "Project", "", now, now, 0, [], [], []);
        var chat = new ChatDetails(chatId, projectId, "Chat", now, now, 0, null, []);
        var global = new GlobalSettings([], [DefaultMcpServer.Settings], []);
        var projects = new Mock<IProjectService>();
        var chats = new Mock<IChatService>();
        var settings = new Mock<IGlobalSettingsRepository>();
        projects.Setup(service => service.GetAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => project);
        chats.Setup(service => service.GetAsync(projectId, chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => chat);
        settings.Setup(repository => repository.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => global);
        var resolver = new ToolPolicyResolver(projects.Object, chats.Object, settings.Object, new ToolDefaultDecision());

        async Task<string> DecisionAsync(string toolName = name) =>
            (await resolver.ResolveAsync(projectId, chatId, serverId, toolName, schema, CancellationToken.None)).Decision;

        (await DecisionAsync()).ShouldBe("Allow");
        (await DecisionAsync("new_tool")).ShouldBe("Ask");

        global = global with { ToolPolicies = [new McpToolPolicySettings(serverId, name, schema, "Deny", 3, 30)] };
        (await DecisionAsync()).ShouldBe("Deny");

        project = project with { ToolPolicies = [new ToolPolicySettings(serverId, name, schema, "Ask", null, null)] };
        (await DecisionAsync()).ShouldBe("Ask");

        chat = chat with { ToolPolicies = [new ToolPolicySettings(serverId, name, schema, "Allow", null, null)] };
        (await DecisionAsync()).ShouldBe("Allow");

        global = global with { McpServers = [DefaultMcpServer.Settings with { Policy = "Deny" }] };
        (await DecisionAsync()).ShouldBe("Deny");
    }
}
