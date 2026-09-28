namespace AI.Web.Tests.Settings;

using AI.Contracts.Settings;
using AI.Web.Settings;
using Shouldly;
using Xunit;

public class SettingsImportPlannerTests
{
    private readonly SettingsTransferCodec _codec = new();
    private readonly SettingsImportPlanner _planner = new();

    private SettingsImportPlan Plan(string json, IReadOnlyList<ConnectionSettings>? connections = null,
        IReadOnlyList<McpServerSettings>? servers = null) =>
        _planner.Plan(_codec.Parse(json), connections ?? [], servers ?? []);

    private static McpServerSettings Github(bool enabled = true, string package = "@mcp/github") =>
        new(Guid.NewGuid(), "github", "Stdio", enabled, "Ask", null, "npx", ["-y", package], null,
            [new McpEnvironmentVariableSettings("GITHUB_TOKEN", null, true, true)], false);

    private const string GithubJson = """{ "mcpServers": { "GitHub": { "command": "npx", "args": ["-y", "@mcp/github"], "env": { "GITHUB_TOKEN": "" } } } }""";

    [Fact]
    public void ShouldAddANewStdioServerSwitchedOff()
    {
        var candidate = Plan(GithubJson).McpServers.Single();

        candidate.Match.ShouldBe(SettingsImportMatch.New);
        candidate.DefaultAction.ShouldBe(SettingsImportAction.Add);
        candidate.AddAs.Enabled.ShouldBeFalse();
        candidate.SwitchedOffIfAdded.ShouldBeTrue();
        candidate.MissingIfAdded.ShouldBe(["GITHUB_TOKEN"]);
    }

    [Fact]
    public void ShouldReplaceByNameKeepingTheIdAndTheSavedSecrets()
    {
        var existing = Github();

        var candidate = Plan(GithubJson, servers: [existing]).McpServers.Single();

        candidate.Match.ShouldBe(SettingsImportMatch.Same);
        candidate.ReplaceWith.ShouldNotBeNull();
        candidate.ReplaceWith.Id.ShouldBe(existing.Id);
        candidate.ReplaceWith.Name.ShouldBe("github");
        candidate.ReplaceWith.Enabled.ShouldBeTrue();
        candidate.MissingIfReplaced.ShouldBeEmpty();
        candidate.DefaultAction.ShouldBe(SettingsImportAction.Skip);
    }

    [Fact]
    public void ShouldLeaveTheSameServerSwitchedOffWhenTheUserTurnedItOff()
    {
        var candidate = Plan(GithubJson, servers: [Github(enabled: false)]).McpServers.Single();

        candidate.Match.ShouldBe(SettingsImportMatch.Same);
        candidate.ReplaceWith!.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void ShouldSwitchOffAServerWhoseCommandChanges()
    {
        var candidate = Plan(GithubJson, servers: [Github(package: "@other/github")]).McpServers.Single();

        candidate.Match.ShouldBe(SettingsImportMatch.Conflict);
        candidate.DefaultAction.ShouldBe(SettingsImportAction.Replace);
        candidate.ReplaceWith!.Enabled.ShouldBeFalse();
        candidate.SwitchedOffIfReplaced.ShouldBeTrue();
    }

    [Fact]
    public void ShouldNeverReplaceTheHostsOwnServers()
    {
        var candidate = Plan("""{ "mcpServers": { "Default tools": { "command": "x" } } }""",
            servers: [DefaultMcpServer.Settings]).McpServers.Single();

        candidate.CanReplace.ShouldBeFalse();
        candidate.DefaultAction.ShouldBe(SettingsImportAction.KeepBoth);
    }

    [Fact]
    public void ShouldKeepAConnectionsKeyAndRolesWhenReplacing()
    {
        var existing = new ConnectionSettings(Guid.NewGuid(), "OpenRouter", "https://old.example.com/v1", "m", true, true, true, true,
            null, null, null, null, null);

        var candidate = Plan("""{ "connections": [{ "name": "openrouter", "baseUrl": "https://openrouter.ai/api/v1", "model": "m", "apiKey": "" }] }""",
            connections: [existing]).Connections.Single();

        candidate.Match.ShouldBe(SettingsImportMatch.Conflict);
        candidate.ReplaceWith!.Id.ShouldBe(existing.Id);
        candidate.ReplaceWith.BaseUrl.ShouldBe("https://openrouter.ai/api/v1");
        candidate.ReplaceWith.IsDefault.ShouldBeTrue();
        candidate.ReplaceWith.HasCredential.ShouldBeTrue();
        candidate.MissingIfAdded.ShouldBe(["API key"]);
        candidate.MissingIfReplaced.ShouldBeEmpty();
    }

    [Fact]
    public void ShouldNumberNamesThatAreTaken() =>
        _planner.UniqueName("github", ["GitHub", "github (2)"]).ShouldBe("github (3)");
}
