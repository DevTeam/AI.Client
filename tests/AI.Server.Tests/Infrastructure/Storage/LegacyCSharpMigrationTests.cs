namespace AI.Infrastructure.Tests.Storage;

using AI.Contracts.Settings;
using AI.Domain.Chats;
using AI.Domain.Projects;
using AI.Infrastructure.Settings;
using AI.Infrastructure.Storage;
using Moq;
using Shouldly;
using Xunit;

/// <summary>
/// Settings, projects and chats written while the optional C# scripting server existed move its
/// <c>cs_run</c> policies to the built-in server, which now owns the tool.
/// </summary>
public sealed class LegacyCSharpMigrationTests
{
    private static readonly Guid Legacy = LegacyCSharpMcpServer.Id;

    [Fact]
    public async Task GlobalSettingsShouldDropTheServerAndMoveItsPolicies()
    {
        var settings = await LoadGlobalAsync(new GlobalSettings([],
            [Server(enabled: true, "Ask")],
            [
                new McpToolPolicySettings(Legacy, "cs_run", "hash", "Allow", 7, 30),
                new McpToolPolicySettings(Legacy, "other", "hash", "Allow", 7, 30)
            ]));

        settings.McpServers.ShouldNotContain(server => server.Id == Legacy);
        settings.McpServers.ShouldContain(server => server.Id == DefaultMcpServer.Id);
        settings.ToolPolicies.ShouldHaveSingleItem()
            .ShouldBe(new McpToolPolicySettings(DefaultMcpServer.Id, "cs_run", "hash", "Allow", 7, 30));
    }

    [Theory]
    [InlineData(false, "Ask")]
    [InlineData(true, "Deny")]
    public async Task GlobalSettingsShouldDenyTheToolWhenTheOldServerWasOff(bool enabled, string policy)
    {
        var settings = await LoadGlobalAsync(new GlobalSettings([], [Server(enabled, policy)],
            [new McpToolPolicySettings(Legacy, "cs_run", "hash", "Allow", 7, 30)]));

        settings.ToolPolicies.ShouldHaveSingleItem()
            .ShouldBe(new McpToolPolicySettings(DefaultMcpServer.Id, "cs_run", "hash", "Deny", 7, 30));
    }

    [Fact]
    public async Task GlobalSettingsShouldAddADenyWhenTheOldServerWasOffWithoutPolicies()
    {
        var settings = await LoadGlobalAsync(new GlobalSettings([], [Server(enabled: false, "Ask")], []));

        var deny = settings.ToolPolicies.ShouldHaveSingleItem();
        deny.ServerId.ShouldBe(DefaultMcpServer.Id);
        deny.Name.ShouldBe("cs_run");
        deny.SchemaHash.ShouldBe(LegacyCSharpToolMigration.ScriptToolSchemaHash);
        deny.Decision.ShouldBe("Deny");
    }

    [Fact]
    public async Task GlobalSettingsShouldKeepAPolicyAlreadyStoredForTheBuiltInTool()
    {
        var settings = await LoadGlobalAsync(new GlobalSettings([], [],
        [
            new McpToolPolicySettings(DefaultMcpServer.Id, "cs_run", "hash", "Ask", 1, 1),
            new McpToolPolicySettings(Legacy, "cs_run", "hash", "Allow", 7, 30)
        ]));

        settings.ToolPolicies.ShouldHaveSingleItem().Decision.ShouldBe("Ask");
    }

    [Fact]
    public async Task GlobalSettingsWithoutTheOldServerShouldStayAsTheyAre()
    {
        var policy = new McpToolPolicySettings(DefaultMcpServer.Id, "read_text_file", "hash", "Allow", 7, 30);

        var settings = await LoadGlobalAsync(new GlobalSettings([], [], [policy]));

        settings.ToolPolicies.ShouldHaveSingleItem().ShouldBe(policy);
    }

    [Theory]
    [InlineData(true, ToolPolicyDecision.Allow)]
    [InlineData(false, ToolPolicyDecision.Deny)]
    public void ProjectShouldDropTheBindingAndMovePolicies(bool enabled, ToolPolicyDecision expected)
    {
        var at = DateTimeOffset.UnixEpoch;
        var project = new Project(new ProjectId(Guid.NewGuid()), "Project", string.Empty, at);
        project.AddMcpServer(new McpServerBinding(new McpServerId(Legacy), "C# scripts", McpTransportKind.Stdio, enabled), at);
        project.SetToolPolicy(new ToolPolicy(new ToolIdentity(new McpServerId(Legacy), "cs_run", "hash"),
            ToolPolicyDecision.Allow, 3), at);
        var serializer = new ProjectDocumentSerializer();

        var restored = serializer.Deserialize(serializer.Serialize(project, 1)).Project;

        restored.McpServers.ShouldHaveSingleItem().Id.Value.ShouldBe(DefaultMcpServer.Id);
        var moved = restored.ToolPolicies.ShouldHaveSingleItem();
        moved.Tool.ServerId.Value.ShouldBe(DefaultMcpServer.Id);
        moved.Tool.Name.ShouldBe("cs_run");
        moved.Tool.SchemaHash.ShouldBe("hash");
        moved.Decision.ShouldBe(expected);
        moved.MaxCallsPerRun.ShouldBe(3);
    }

    [Fact]
    public void ProjectShouldDenyTheToolWhenTheOldServerWasSwitchedOffWithoutPolicies()
    {
        var at = DateTimeOffset.UnixEpoch;
        var project = new Project(new ProjectId(Guid.NewGuid()), "Project", string.Empty, at);
        project.AddMcpServer(new McpServerBinding(new McpServerId(Legacy), "C# scripts", McpTransportKind.Stdio, false), at);
        var serializer = new ProjectDocumentSerializer();

        var restored = serializer.Deserialize(serializer.Serialize(project, 1)).Project;

        var deny = restored.ToolPolicies.ShouldHaveSingleItem();
        deny.Tool.ServerId.Value.ShouldBe(DefaultMcpServer.Id);
        deny.Tool.SchemaHash.ShouldBe(LegacyCSharpToolMigration.ScriptToolSchemaHash);
        deny.Decision.ShouldBe(ToolPolicyDecision.Deny);
    }

    [Fact]
    public void ChatShouldMovePolicies()
    {
        var chat = new ChatThread(new ChatId(Guid.NewGuid()), new ProjectId(Guid.NewGuid()), "Chat", DateTimeOffset.UnixEpoch);
        chat.SetToolPolicy(new ToolPolicy(new ToolIdentity(new McpServerId(Legacy), "cs_run", "hash"),
            ToolPolicyDecision.Deny), DateTimeOffset.UnixEpoch);
        var serializer = new ChatDocumentSerializer();

        var restored = serializer.Deserialize(serializer.Serialize(chat, 1)).Chat;

        var moved = restored.ToolPolicies.ShouldHaveSingleItem();
        moved.Tool.ServerId.Value.ShouldBe(DefaultMcpServer.Id);
        moved.Tool.Name.ShouldBe("cs_run");
        moved.Decision.ShouldBe(ToolPolicyDecision.Deny);
    }

    private static McpServerSettings Server(bool enabled, string policy) =>
        new(Legacy, "C# scripts", "Stdio", enabled, policy, null, null, [], null, [], false);

    private static async Task<GlobalSettings> LoadGlobalAsync(GlobalSettings stored)
    {
        var fileSystem = new MemoryFileSystem();
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(item => item.RootDirectory).Returns("data");
        var paths = new GlobalSettingsPaths(location.Object);
        fileSystem.Files[paths.SettingsPath] = System.Text.Json.JsonSerializer.Serialize(stored);
        using var repository = new JsonGlobalSettingsRepository(fileSystem, paths);
        return await repository.LoadAsync(CancellationToken.None);
    }
}
