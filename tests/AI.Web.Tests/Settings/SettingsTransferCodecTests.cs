namespace AI.Web.Tests.Settings;

using System.Text.Json;
using AI.Contracts.Settings;
using AI.Web.Settings;
using Shouldly;
using Xunit;

public class SettingsTransferCodecTests
{
    private readonly SettingsTransferCodec _codec = new();

    private static ConnectionSettings Connection(string name = "OpenRouter", bool hasCredential = true) =>
        new(Guid.NewGuid(), name, "https://openrouter.ai/api/v1", "gpt-5", true, true, hasCredential, true,
            4, "long context", 200_000, null);

    private static McpServerSettings Stdio(params McpEnvironmentVariableSettings[] environment) =>
        new(Guid.NewGuid(), "github", "Stdio", true, "Allow", null, "npx", ["-y", "@mcp/github"], null, environment, false);

    [Fact]
    public void ShouldExportMcpServersInTheSharedShapeWithoutSecretValues()
    {
        var json = _codec.Export([], [Stdio(
            new McpEnvironmentVariableSettings("GITHUB_TOKEN", null, true, true),
            new McpEnvironmentVariableSettings("LOG_LEVEL", "info", false, false))]);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.TryGetProperty("connections", out _).ShouldBeFalse();
        var server = root.GetProperty("mcpServers").GetProperty("github");
        server.GetProperty("command").GetString().ShouldBe("npx");
        server.GetProperty("args").EnumerateArray().Select(item => item.GetString()).ShouldBe(["-y", "@mcp/github"]);
        server.GetProperty("env").GetProperty("GITHUB_TOKEN").GetString().ShouldBe(string.Empty);
        server.GetProperty("env").GetProperty("LOG_LEVEL").GetString().ShouldBe("info");
        server.GetProperty("policy").GetString().ShouldBe("Allow");
        server.TryGetProperty("secretEnv", out _).ShouldBeFalse();
        server.TryGetProperty("disabled", out _).ShouldBeFalse();
    }

    [Fact]
    public void ShouldNameSecretsTheNameDoesNotGiveAway()
    {
        var json = _codec.Export([], [Stdio(new McpEnvironmentVariableSettings("DATABASE_URL", null, true, true))]);

        var parsed = _codec.Parse(json);

        parsed.McpServers.Single().Settings.EnvironmentVariables.Single().IsSecret.ShouldBeTrue();
        parsed.SecretValuesDropped.ShouldBeFalse();
    }

    [Fact]
    public void ShouldExportConnectionsWithoutTheirKeyOrLocalRoles()
    {
        var json = _codec.Export([Connection()], []);

        using var document = JsonDocument.Parse(json);
        var connection = document.RootElement.GetProperty("connections")[0];
        connection.GetProperty("name").GetString().ShouldBe("OpenRouter");
        connection.GetProperty("apiKey").GetString().ShouldBe(string.Empty);
        connection.GetProperty("contextWindowTokens").GetInt64().ShouldBe(200_000);
        connection.TryGetProperty("isDefault", out _).ShouldBeFalse();
        connection.TryGetProperty("forSubtasks", out _).ShouldBeFalse();
        json.ShouldNotContain("\"id\"", Case.Insensitive);
    }

    [Fact]
    public void ShouldRoundTripWhatItExports()
    {
        var connection = Connection();
        var http = new McpServerSettings(Guid.NewGuid(), "docs", "StreamableHttp", false, "Deny", "https://example.com/mcp",
            null, [], null, [], true);

        var parsed = _codec.Parse(_codec.Export([connection], [Stdio(), http]));

        parsed.Error.ShouldBeNull();
        var imported = parsed.Connections.Single();
        imported.Settings.ShouldBe(connection with
        {
            Id = imported.Settings.Id, IsDefault = false, ForSubtasks = false, HasCredential = false
        });
        imported.CredentialOmitted.ShouldBeTrue();
        var docs = parsed.McpServers.Single(item => item.Settings.Name == "docs");
        docs.Settings.Transport.ShouldBe("StreamableHttp");
        docs.Settings.Url.ShouldBe("https://example.com/mcp");
        docs.Settings.Enabled.ShouldBeFalse();
        docs.Settings.Policy.ShouldBe("Deny");
        docs.CredentialOmitted.ShouldBeTrue();
        parsed.SecretValuesDropped.ShouldBeFalse();
    }

    [Fact]
    public void ShouldLeaveOutKeysPastedFromAReadme()
    {
        var parsed = _codec.Parse("""
            {
              // comments are allowed, as in VS Code
              "mcpServers": {
                "github": {
                  "command": "npx",
                  "args": ["-y", "@modelcontextprotocol/server-github"],
                  "env": { "GITHUB_PERSONAL_ACCESS_TOKEN": "ghp_realLookingValue123456", "PLAIN": "1" },
                },
              }
            }
            """);

        var server = parsed.McpServers.Single().Settings;
        var token = server.EnvironmentVariables.Single(item => item.Name == "GITHUB_PERSONAL_ACCESS_TOKEN");
        token.IsSecret.ShouldBeTrue();
        token.Value.ShouldBeNull();
        server.EnvironmentVariables.Single(item => item.Name == "PLAIN").Value.ShouldBe("1");
        parsed.SecretValuesDropped.ShouldBeTrue();
        parsed.ToString().ShouldNotContain("ghp_realLookingValue123456");
    }

    [Theory]
    [InlineData("<YOUR_TOKEN>")]
    [InlineData("${input:github-token}")]
    [InlineData("your-api-key")]
    [InlineData("")]
    [InlineData("xxxxxxxx")]
    public void ShouldNotWarnAboutPlaceholders(string value)
    {
        var parsed = _codec.Parse($$"""{ "mcpServers": { "s": { "command": "run", "env": { "API_KEY": "{{value}}" } } } }""");

        parsed.McpServers.Single().Settings.EnvironmentVariables.Single().IsSecret.ShouldBeTrue();
        parsed.SecretValuesDropped.ShouldBeFalse();
    }

    [Fact]
    public void ShouldReadVsCodeServersAndHeaders()
    {
        var parsed = _codec.Parse("""
            {
              "inputs": [{ "id": "token", "type": "promptString", "password": true }],
              "servers": {
                "remote": { "type": "http", "url": "https://mcp.example.com", "headers": { "Authorization": "Bearer ${input:token}", "X-Region": "eu" } }
              }
            }
            """);

        var remote = parsed.McpServers.Single();
        remote.Settings.Transport.ShouldBe("StreamableHttp");
        remote.CredentialOmitted.ShouldBeTrue();
        remote.Notes.ShouldContain(note => note.Contains("X-Region"));
        parsed.SecretValuesDropped.ShouldBeFalse();
    }

    [Fact]
    public void ShouldReadAServerCopiedWithoutTheWrapper()
    {
        var parsed = _codec.Parse("""
            "fetch": { "command": "uvx", "args": ["mcp-server-fetch"] }
            """);

        parsed.McpServers.Single().Settings.Name.ShouldBe("fetch");
    }

    [Fact]
    public void ShouldNameALoneServerAfterItsPackage()
    {
        var parsed = _codec.Parse("""{ "command": "npx", "args": ["-y", "@modelcontextprotocol/server-github@latest"] }""");

        parsed.McpServers.Single().Settings.Name.ShouldBe("server-github");
    }

    [Fact]
    public void ShouldSkipEntriesItCannotUseAndSayWhy()
    {
        var parsed = _codec.Parse("""
            {
              "mcpServers": { "broken": { "type": "websocket" }, "ok": { "url": "https://ok.example.com/mcp" } },
              "connections": [{ "name": "local", "baseUrl": "not a url", "model": "m" }]
            }
            """);

        parsed.McpServers.Single().Settings.Name.ShouldBe("ok");
        parsed.Skipped.Select(item => item.Name).ShouldBe(["broken", "local"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[1, 2]")]
    [InlineData("{ \"theme\": \"dark\" }")]
    public void ShouldExplainWhenThereIsNothingToImport(string text) =>
        _codec.Parse(text).Error.ShouldNotBeNull();

    [Fact]
    public void ShouldPointAtBrokenJson()
    {
        var parsed = _codec.Parse("{\n  \"mcpServers\": {\n    \"a\": { \"command\": }\n  }\n}");

        parsed.Error.ShouldNotBeNull();
        parsed.Error.ShouldContain("line 3");
    }
}
