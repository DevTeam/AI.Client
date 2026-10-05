namespace AI.Infrastructure.Tests.Tools;

using AI.Application.Chat;
using AI.Application.Chats;
using AI.Application.Instructions;
using AI.Application.Notifications;
using AI.Application.Projects;
using AI.Application.Runs;
using AI.Application.Settings;
using AI.Application.Tools;
using AI.Application.Workspace;
using AI.Contracts.Chats;
using AI.Contracts.Navigation;
using AI.Contracts.Projects;
using AI.Contracts.Runs;
using AI.Contracts.Settings;
using AI.Contracts.Tools;
using AI.Domain.Chats;
using AI.Infrastructure.Projects;
using AI.Infrastructure.Chat;
using AI.Infrastructure.Settings;
using AI.Infrastructure.Storage;
using AI.Infrastructure.Tests.Storage;
using AI.Infrastructure.Tools;
using AI.Infrastructure.Workspace;
using AI.Mcp.App;
using AI.Server.Hosting;
using Moq;
using Shouldly;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

/// <summary>
/// Exercises the application server the way the Host does: over a real MCP session, through the
/// protocol's own <c>tools/list</c> and <c>tools/call</c>, against real application services.
/// Nothing here reaches into a tool class directly, so a schema or transport regression fails here
/// rather than in production.
/// </summary>
public sealed class AppToolTests
{
    [Fact]
    public async Task ShouldDiscoverExactToolPolicyIdentitiesWithoutCallingTools()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var first = await AppFixture.CallAsync(session, "app_read",
            new { resource = "McpTools", resourceId = AppMcpServer.Id, limit = 1 });
        first.GetProperty("total").GetInt32().ShouldBeGreaterThan(1);
        var tool = first.GetProperty("items")[0];
        tool.GetProperty("serverId").GetGuid().ShouldBe(AppMcpServer.Id);
        var declared = session.Tools.Single(item => item.OriginalName == tool.GetProperty("name").GetString());
        tool.GetProperty("schemaHash").GetString().ShouldBe(declared.SchemaHash);
        tool.GetProperty("inputSchema").GetProperty("type").GetString().ShouldBe("object");
        tool.GetProperty("annotations").ValueKind.ShouldBe(JsonValueKind.Object);

        var next = await AppFixture.CallAsync(session, "app_read",
            new { resource = "McpTools", resourceId = AppMcpServer.Id, limit = 1,
                cursor = first.GetProperty("nextCursor").GetString() });
        next.GetProperty("items")[0].GetProperty("name").GetString()
            .ShouldNotBe(tool.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData(false, "Ask")]
    [InlineData(true, "Deny")]
    public async Task ShouldRejectDiscoveryOfDisabledOrUnknownServers(bool enabled, string policy)
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        var global = await fixture.GlobalSettings.GetAsync(CancellationToken.None);
        await fixture.GlobalSettings.SaveAsync(new SaveGlobalSettingsRequest(global.Connections,
            global.McpServers.Select(server => server.Id == DefaultMcpServer.Id
                ? server with { Enabled = enabled, Policy = policy } : server).ToArray(), global.ToolPolicies), CancellationToken.None);

        var disabled = await AppFixture.CallAsync(session, "app_read",
            new { resource = "McpTools", resourceId = DefaultMcpServer.Id }, expectError: true);
        disabled.GetProperty("error").GetString()!.ShouldContain("disabled or denied");
        var missing = await AppFixture.CallAsync(session, "app_read",
            new { resource = "McpTools", resourceId = Guid.NewGuid() }, expectError: true);
        missing.GetProperty("error").GetString()!.ShouldContain("not found");
    }

    [Fact]
    public async Task ShouldReadAndPageModelsForANewEndpointWithoutSavingConnections()
    {
        await using var fixture = await AppFixture.CreateAsync();
        fixture.Models.Setup(item => item.ResolveAsync("https://provider.test/int/v1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ResolvedModelInfo("first"), new ResolvedModelInfo("second")]);
        await using var session = await fixture.OpenAsync();

        var first = await AppFixture.CallAsync(session, "app_read",
            new { resource = "ConnectionModels", query = "https://provider.test/int/v1", limit = 1 });
        first.GetProperty("total").GetInt32().ShouldBe(2);
        first.GetProperty("items")[0].GetProperty("id").GetString().ShouldBe("first");
        var second = await AppFixture.CallAsync(session, "app_read",
            new { resource = "ConnectionModels", query = "https://provider.test/int/v1", limit = 1,
                cursor = first.GetProperty("nextCursor").GetString() });
        second.GetProperty("items")[0].GetProperty("id").GetString().ShouldBe("second");
        var settings = await AppFixture.CallAsync(session, "app_read", new { resource = "Settings" });
        settings.GetProperty("items")[0].GetProperty("connections").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task ShouldUseOnlyTheSavedEndpointForConnectionModelDiscovery()
    {
        await using var fixture = await AppFixture.CreateAsync();
        fixture.Secrets.Setup(item => item.GetAsync("connection", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("saved-key");
        fixture.Models.Setup(item => item.ResolveAsync("https://example.test/v1", "saved-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ResolvedModelInfo("model")]);
        await using var session = await fixture.OpenAsync();
        var settings = await AppFixture.CallAsync(session, "app_read", new { resource = "Settings" });
        var id = settings.GetProperty("items")[0].GetProperty("connections")[0].GetProperty("id").GetGuid();

        var result = await AppFixture.CallAsync(session, "app_read", new { resource = "ConnectionModels", resourceId = id });
        result.GetProperty("items")[0].GetProperty("id").GetString().ShouldBe("model");
        fixture.Models.Verify(item => item.ResolveAsync("https://example.test/v1", "saved-key", It.IsAny<CancellationToken>()), Times.Once);
        result.GetRawText().ShouldNotContain("saved-key");

        var rejected = await AppFixture.CallAsync(session, "app_read",
            new { resource = "ConnectionModels", resourceId = id, query = "https://other.test/v1" }, expectError: true);
        rejected.GetProperty("error").GetString()!.ShouldContain("resourceId alone");
        fixture.Models.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ShouldReportModelDiscoveryErrorsWithoutMutatingSettings()
    {
        await using var fixture = await AppFixture.CreateAsync();
        fixture.Models.Setup(item => item.ResolveAsync("https://provider.test/v1", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The endpoint returned 401 (Unauthorized)."));
        await using var session = await fixture.OpenAsync();

        var failed = await AppFixture.CallAsync(session, "app_read",
            new { resource = "ConnectionModels", query = "https://provider.test/v1" }, expectError: true);
        failed.GetProperty("error").GetString()!.ShouldContain("401");
        var missing = await AppFixture.CallAsync(session, "app_read", new { resource = "ConnectionModels" }, expectError: true);
        missing.GetProperty("error").GetString()!.ShouldContain("required");
        var unknown = await AppFixture.CallAsync(session, "app_read",
            new { resource = "ConnectionModels", resourceId = Guid.NewGuid() }, expectError: true);
        unknown.GetProperty("error").GetString().ShouldBe("Connection not found.");
    }

    [Fact]
    public async Task ShouldDiscoverEveryToolOverTheInProcessTransport()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        // The server decides its own listing order, so the set is what matters, not the sequence.
        session.Tools.Select(tool => tool.OriginalName).Order(StringComparer.Ordinal).ShouldBe(
            ["app_chats", "app_instructions", "app_memory", "app_navigate", "app_projects", "app_read", "app_resources", "app_runs", "app_security", "app_skills", "ask_user", "context_compact", "run_skill", "skill_search", "spawn_subtask", "tool_search"]);
        session.Tools.ShouldAllBe(tool => tool.ServerId == AppMcpServer.Id);
        session.Tools.ShouldAllBe(tool => tool.ModelDefinition.Name.StartsWith("mcp_app__", StringComparison.Ordinal));
        var search = session.Tools.Single(tool => tool.OriginalName == "tool_search");
        search.ModelDefinition.Name.ShouldBe(AI.Contracts.Tools.ToolRef.ToolSearchName);
        search.ModelDefinition.Description.ShouldContain("Call " + search.ModelDefinition.Name);
        search.ModelDefinition.Description.ShouldNotContain("app_tool_search");
        // A schema hash is what ties a saved policy to the tool it was granted for.
        session.Tools.ShouldAllBe(tool => tool.SchemaHash.Length == 64);
    }

    [Fact]
    public async Task ProjectToolShouldDescribeDirectoryBasedNameSuggestion()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var description = session.Tools.Single(tool => tool.OriginalName == "app_projects").ModelDefinition.Description;

        description.ShouldContain("derive a suggested project name");
        description.ShouldContain("final segment");
        description.ShouldContain("(Recommended)");
        description.ShouldContain("allow a custom name");
    }

    [Fact]
    public async Task ShouldReadProjectsAndChats()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var projects = await AppFixture.CallAsync(session, "app_read", new { resource = "Projects" });
        projects.GetProperty("total").GetInt32().ShouldBe(1);
        projects.GetProperty("items")[0].GetProperty("name").GetString().ShouldBe("Test");

        var chats = await AppFixture.CallAsync(session, "app_read", new { resource = "Chats", projectId = fixture.ProjectId });
        chats.GetProperty("items")[0].GetProperty("title").GetString().ShouldBe("Chat");
    }

    [Fact]
    public async Task ShouldDefaultProjectScopedReadsToTheCurrentProject()
    {
        await using var fixture = await AppFixture.CreateAsync();
        var otherProject = await fixture.Projects.CreateAsync(new CreateProjectRequest("Other", ""), CancellationToken.None);
        await using var session = await fixture.OpenAsync();

        var current = await AppFixture.CallAsync(session, "app_read", new { resource = "Project" });
        current.GetProperty("items")[0].GetProperty("id").GetGuid().ShouldBe(fixture.ProjectId);

        var explicitProject = await AppFixture.CallAsync(session, "app_read",
            new { resource = "Project", projectId = otherProject.Id });
        explicitProject.GetProperty("items")[0].GetProperty("id").GetGuid().ShouldBe(otherProject.Id);

        var chats = await AppFixture.CallAsync(session, "app_read", new { resource = "Chats" });
        chats.GetProperty("items")[0].GetProperty("id").GetGuid().ShouldBe(fixture.ChatId);

        var description = session.Tools.Single(tool => tool.OriginalName == "app_read").ModelDefinition.Description;
        description.ShouldContain("omitted projectId means the current project");
        description.ShouldContain("'Review' needs resourceId");
    }

    [Fact]
    public async Task ShouldNotReturnSecretsWhenReadingSettings()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var settings = await AppFixture.CallAsync(session, "app_read", new { resource = "Settings" });

        var connection = settings.GetProperty("items")[0].GetProperty("connections")[0];
        connection.TryGetProperty("hasCredential", out _).ShouldBeTrue();
        // Whatever shape the settings document grows, a key must never be part of it.
        settings.GetRawText().ShouldNotContain("apiKey", Case.Insensitive);
        settings.GetRawText().ShouldNotContain("secret", Case.Insensitive);
    }

    [Fact]
    public async Task ShouldPageAndContinueFromTheCursorItReturned()
    {
        await using var fixture = await AppFixture.CreateAsync();
        for (var index = 0; index < 5; index++)
            await fixture.Chats.CreateAsync(fixture.ProjectId, new CreateChatRequest($"Chat {index}"), CancellationToken.None);
        await using var session = await fixture.OpenAsync();

        var first = await AppFixture.CallAsync(session, "app_read",
            new { resource = "Chats", projectId = fixture.ProjectId, limit = 2 });
        first.GetProperty("returned").GetInt32().ShouldBe(2);
        first.GetProperty("total").GetInt32().ShouldBe(6);
        var cursor = first.GetProperty("nextCursor").GetString();
        cursor.ShouldNotBeNull();

        var second = await AppFixture.CallAsync(session, "app_read",
            new { resource = "Chats", projectId = fixture.ProjectId, limit = 2, cursor });
        second.GetProperty("returned").GetInt32().ShouldBe(2);
        // The second page must not repeat the first one's items.
        var firstIds = first.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray();
        second.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()).ShouldAllBe(id => !firstIds.Contains(id));
    }

    [Fact]
    public async Task ShouldCreateAChatAndAnnounceTheChange()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        using var changed = fixture.WatchChanges();

        var result = await AppFixture.CallAsync(session, "app_chats",
            new { operation = "Create", projectId = fixture.ProjectId, operationId = Guid.NewGuid(), title = "From a tool" });

        result.GetProperty("applied").GetBoolean().ShouldBeTrue();
        var chats = await fixture.Chats.ListAsync(fixture.ProjectId, CancellationToken.None);
        chats.ShouldContain(chat => chat.Title == "From a tool");
        (await changed.WaitAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task ToolSearchShouldPointADrawingRequestAtDiagramBlocks()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var drawing = await AppFixture.CallAsync(session, "tool_search", new { query = "draw architecture diagram" });
        var unrelated = await AppFixture.CallAsync(session, "tool_search", new { query = "translate spreadsheet cells" });

        drawing.GetProperty("tools").GetArrayLength().ShouldBe(0);
        drawing.GetProperty("guidance").GetString()!.ShouldContain("```mermaid");
        unrelated.GetProperty("guidance").GetString()!.ShouldNotContain("mermaid");
    }

    [Fact]
    public async Task ShouldRememberCorrectAndForgetAMemoryEntry()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var created = await AppFixture.CallAsync(session, "app_memory", new
        {
            operation = "Create", operationId = Guid.NewGuid(), scope = "User", kind = "Profile",
            title = "Name", body = "The user is called Nikolay."
        });
        created.GetProperty("applied").GetBoolean().ShouldBeTrue();
        var id = created.GetProperty("current").GetProperty("id").GetGuid();

        var found = await AppFixture.CallAsync(session, "app_read", new { resource = "Memory", query = "nikolay" });
        found.GetProperty("items").GetArrayLength().ShouldBe(1);
        found.GetProperty("items")[0].GetProperty("author").GetString().ShouldBe("Model");
        found.GetProperty("items")[0].GetProperty("chatId").GetGuid().ShouldBe(fixture.ChatId);

        // Only the body is sent; the kind and the pin survive the correction.
        var updated = await AppFixture.CallAsync(session, "app_memory", new
        {
            operation = "Update", operationId = Guid.NewGuid(), resourceId = id, revision = 1, body = "The user is called Kolya."
        });
        updated.GetProperty("current").GetProperty("kind").GetString().ShouldBe("Profile");
        updated.GetProperty("revision").GetInt64().ShouldBe(2);

        var stale = await AppFixture.CallAsync(session, "app_memory", new
        {
            operation = "Delete", operationId = Guid.NewGuid(), resourceId = id, revision = 1
        }, expectError: true);
        stale.GetProperty("status").GetString().ShouldBe("Conflict");

        await AppFixture.CallAsync(session, "app_memory", new
        {
            operation = "Delete", operationId = Guid.NewGuid(), resourceId = id, revision = 2
        });
        var empty = await AppFixture.CallAsync(session, "app_read", new { resource = "Memory" });
        empty.GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task ShouldReplaceProjectInstructionsAndShowThemInTheStandingPrompt()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var before = await AppFixture.CallAsync(session, "app_read", new { resource = "Instructions" });
        var revision = before.GetProperty("items")[0].GetProperty("instructions").GetProperty("revision").GetInt64();
        revision.ShouldBe(0);

        var result = await AppFixture.CallAsync(session, "app_instructions", new
        {
            operationId = Guid.NewGuid(), text = "Answer in Russian. Run the tests before finishing.", revision
        });
        result.GetProperty("applied").GetBoolean().ShouldBeTrue();

        var preview = await fixture.Standing.BuildAsync(fixture.ProjectId, true, TestContext.Current.CancellationToken);
        preview.Layers.Select(layer => layer.Key).ShouldBe(["app.base", "project.instructions", "memory.index", "skills.catalog"]);
        preview.Layers[1].Content.ShouldContain("Run the tests before finishing.");
        var description = session.Tools.Single(tool => tool.OriginalName == "app_instructions").ModelDefinition.Description;
        description.ShouldContain("app_memory");
    }

    [Fact]
    public async Task ShouldReplayARepeatedOperationInsteadOfApplyingItTwice()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        var operationId = Guid.NewGuid();
        var arguments = new { operation = "Create", projectId = fixture.ProjectId, operationId, title = "Only once" };

        var first = await AppFixture.CallAsync(session, "app_chats", arguments);
        var second = await AppFixture.CallAsync(session, "app_chats", arguments);

        second.GetProperty("replayed").GetBoolean().ShouldBeTrue();
        second.GetProperty("chatId").GetString().ShouldBe(first.GetProperty("chatId").GetString());
        (await fixture.Chats.ListAsync(fixture.ProjectId, CancellationToken.None))
            .Count(chat => chat.Title == "Only once").ShouldBe(1);
    }

    [Fact]
    public async Task ShouldOnlyDescribeADeletionUntilDryRunIsTurnedOff()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);

        var planned = await AppFixture.CallAsync(session, "app_chats", new
        {
            operation = "Delete", projectId = fixture.ProjectId, chatId = fixture.ChatId,
            operationId = Guid.NewGuid(), revision = chat!.Revision,
        });

        planned.GetProperty("dryRun").GetBoolean().ShouldBeTrue();
        planned.GetProperty("applied").GetBoolean().ShouldBeFalse();
        planned.GetProperty("effect").GetString().ShouldNotBeNull().ShouldContain("Would delete");
        (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None)).ShouldNotBeNull();

        var applied = await AppFixture.CallAsync(session, "app_chats", new
        {
            operation = "Delete", projectId = fixture.ProjectId, chatId = fixture.ChatId,
            operationId = Guid.NewGuid(), revision = chat.Revision, dryRun = false,
        });

        applied.GetProperty("applied").GetBoolean().ShouldBeTrue();
        (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task ShouldReportTheCurrentStateWhenTheRevisionIsStale()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var result = await AppFixture.CallAsync(session, "app_chats", new
        {
            operation = "Rename", projectId = fixture.ProjectId, chatId = fixture.ChatId,
            operationId = Guid.NewGuid(), title = "Renamed", revision = 999L,
        }, expectError: true);

        result.GetProperty("status").GetString().ShouldBe("Conflict");
        result.GetProperty("applied").GetBoolean().ShouldBeFalse();
        // The caller is handed what it needs to decide for itself, rather than a bare failure.
        result.GetProperty("revision").GetInt64().ShouldBeGreaterThan(0);
        result.GetProperty("current").GetProperty("title").GetString().ShouldBe("Chat");
    }

    [Fact]
    public async Task ShouldChangeProjectSecurity()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        var project = await fixture.Projects.GetAsync(fixture.ProjectId, CancellationToken.None);

        var result = await AppFixture.CallAsync(session, "app_security", new
        {
            operation = "SetProjectSecurity",
            projectId = fixture.ProjectId,
            operationId = Guid.NewGuid(),
            revision = project!.Revision,
            security = new
            {
                directoryGrants = new[]
                {
                    new { id = Guid.NewGuid(), displayName = "src", canonicalRoot = @"C:\Projects\Demo", recursive = true, toolNames = ReadOnlyCapability },
                },
                mcpServers = Array.Empty<object>(),
                toolPolicies = Array.Empty<object>(),
            },
        });

        result.GetProperty("applied").GetBoolean().ShouldBeTrue();
        var updated = await fixture.Projects.GetAsync(fixture.ProjectId, CancellationToken.None);
        updated!.DirectoryGrants.ShouldHaveSingleItem().CanonicalRoot.ShouldBe(@"C:\Projects\Demo");
    }

    [Fact]
    public async Task AddingDirectoryGrantPreservesServerBindingsAndPolicies()
    {
        await using var fixture = await AppFixture.CreateAsync();
        var original = (await fixture.Projects.GetAsync(fixture.ProjectId, CancellationToken.None))!;
        var seeded = await fixture.Projects.UpdateSecurityAsync(fixture.ProjectId,
            new UpdateProjectSecurityRequest(original.Revision,
                [new DirectoryGrantSettings(Guid.NewGuid(), "existing", @"C:\Projects\Existing", true, ["read"])],
                [new AI.Contracts.Projects.McpServerSettings(DefaultMcpServer.Id, "Built-in tools", "Stdio", true)],
                [new ToolPolicySettings(DefaultMcpServer.Id, "read_text_file", "schema", "Allow", 10, 60)]),
            CancellationToken.None);
        await using var session = await fixture.OpenAsync();

        var result = await AppFixture.CallAsync(session, "app_security", new
        {
            operation = "AddDirectoryGrant", projectId = fixture.ProjectId,
            operationId = Guid.NewGuid(), revision = seeded.Revision,
            directoryGrant = new
            {
                id = Guid.NewGuid(), displayName = "sample", canonicalRoot = @"C:\Projects\Sample",
                recursive = true, toolNames = ReadOnlyCapability,
            },
        });

        result.GetProperty("applied").GetBoolean().ShouldBeTrue();
        var updated = (await fixture.Projects.GetAsync(fixture.ProjectId, CancellationToken.None))!;
        updated.DirectoryGrants.Count.ShouldBe(2);
        updated.McpServers.ShouldHaveSingleItem().Id.ShouldBe(DefaultMcpServer.Id);
        updated.ToolPolicies.ShouldHaveSingleItem().Name.ShouldBe("read_text_file");
    }

    [Fact]
    public async Task RemovingDirectoryGrantKeepsTheOtherGrantsBindingsAndPolicies()
    {
        await using var fixture = await AppFixture.CreateAsync();
        var original = (await fixture.Projects.GetAsync(fixture.ProjectId, CancellationToken.None))!;
        var removedId = Guid.NewGuid();
        var seeded = await fixture.Projects.UpdateSecurityAsync(fixture.ProjectId,
            new UpdateProjectSecurityRequest(original.Revision,
                [new DirectoryGrantSettings(removedId, "old", @"C:\Projects\Old", true, ["read"]),
                    new DirectoryGrantSettings(Guid.NewGuid(), "kept", @"C:\Projects\Kept", true, ["read"])],
                [new AI.Contracts.Projects.McpServerSettings(DefaultMcpServer.Id, "Built-in tools", "Stdio", true)],
                [new ToolPolicySettings(DefaultMcpServer.Id, "read_text_file", "schema", "Allow", 10, 60)]),
            CancellationToken.None);
        await using var session = await fixture.OpenAsync();

        var result = await AppFixture.CallAsync(session, "app_security", new
        {
            operation = "RemoveDirectoryGrant", projectId = fixture.ProjectId,
            operationId = Guid.NewGuid(), revision = seeded.Revision, grantId = removedId,
        });

        result.GetProperty("applied").GetBoolean().ShouldBeTrue();
        result.GetProperty("effect").GetString()!.ShouldContain(@"C:\Projects\Old");
        var updated = (await fixture.Projects.GetAsync(fixture.ProjectId, CancellationToken.None))!;
        updated.DirectoryGrants.ShouldHaveSingleItem().DisplayName.ShouldBe("kept");
        updated.McpServers.ShouldHaveSingleItem().Id.ShouldBe(DefaultMcpServer.Id);
        updated.ToolPolicies.ShouldHaveSingleItem().Name.ShouldBe("read_text_file");
    }

    [Fact]
    public async Task ShouldReadSkillDocumentsOnlyWhenAskedForOne()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var catalog = await AppFixture.CallAsync(session, "app_read", new { resource = "Skills", limit = 50 });
        var one = await AppFixture.CallAsync(session, "app_read", new { resource = "Skills", query = "skill-create" });

        catalog.GetProperty("items").EnumerateArray().ShouldAllBe(item => item.GetProperty("content").ValueKind == JsonValueKind.Null);
        var skill = one.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        skill.GetProperty("kind").GetString().ShouldBe("playbook");
        skill.GetProperty("content").GetString()!.ShouldContain("Skill conventions");
    }

    [Fact]
    public async Task ShouldFindMessagesWithoutReadingChatsOneByOne()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await fixture.Chats.AppendMessageAsync(fixture.ProjectId, fixture.ChatId,
            new AppendChatMessageRequest(null, null, "User", "remember the deploy checklist", 1), CancellationToken.None);
        await using var session = await fixture.OpenAsync();

        var found = await AppFixture.CallAsync(session, "app_read", new { resource = "Search", query = "deploy" });

        found.GetProperty("resource").GetString().ShouldBe("Search");
        var match = found.GetProperty("items")[0];
        match.GetProperty("chatId").GetString().ShouldBe(fixture.ChatId.ToString());
        match.GetProperty("snippet").GetString().ShouldNotBeNull().ShouldContain("deploy");
    }

    [Fact]
    public async Task ShouldRefuseASearchWithNoQuery()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var result = await AppFixture.CallAsync(session, "app_read", new { resource = "Search" }, expectError: true);

        result.GetProperty("error").GetString().ShouldNotBeNull().ShouldContain("query");
    }

    [Fact]
    public async Task ShouldRefuseToRehearseAnOperationThatCannotBeRehearsed()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var result = await AppFixture.CallAsync(session, "app_chats", new
        {
            operation = "Create", projectId = fixture.ProjectId, operationId = Guid.NewGuid(),
            title = "Rehearsed", dryRun = true,
        }, expectError: true);

        // Asking for a rehearsal that cannot happen must fail loudly; applying the change instead
        // would be exactly the surprise the flag exists to prevent.
        result.GetProperty("error").GetString().ShouldNotBeNull().ShouldContain("cannot be rehearsed");
        (await fixture.Chats.ListAsync(fixture.ProjectId, CancellationToken.None))
            .ShouldNotContain(chat => chat.Title == "Rehearsed");
    }

    [Fact]
    public async Task ShouldRejectArgumentsThatDoNotMatchTheDeclaredSchema()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        var tool = session.Tools.Single(item => item.OriginalName == "app_read");

        // 'resource' is a closed enum in the schema, so the Host rejects this before the server sees it.
        var error = Should.Throw<ArgumentException>(() => session.ValidateArguments(tool, """{"resource":"Everything"}"""));
        // The message has to say what was wrong with which property, or the caller can only guess.
        error.Message.ShouldContain("resource");
    }

    [Fact]
    public async Task ShouldRepairEnumCaseAndObjectsSentAsJsonText()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        var read = session.Tools.Single(item => item.OriginalName == "app_read");
        var ask = session.Tools.Single(item => item.OriginalName == "ask_user");

        var repairedEnum = JsonNode.Parse(session.ValidateArguments(read, """{"resource":"projects"}"""))!;
        var questions = JsonSerializer.Serialize(new[] { new { id = "q", text = "Which?", options = new[] { new { label = "A" } } } });
        var repairedArray = JsonNode.Parse(session.ValidateArguments(ask, JsonSerializer.Serialize(new { questions })))!;

        repairedEnum["resource"]!.GetValue<string>().ShouldBe("Projects");
        repairedArray["questions"].ShouldBeOfType<JsonArray>().Count.ShouldBe(1);
    }

    [Fact]
    public async Task ShouldReadTheCurrentChatWhenNoChatIsNamed()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var chat = await AppFixture.CallAsync(session, "app_read", new { resource = "Chat" });

        chat.GetProperty("items")[0].GetProperty("id").GetGuid().ShouldBe(fixture.ChatId);
    }

    private static readonly IReadOnlySet<Guid> AppServerOnly = new HashSet<Guid> { AppMcpServer.Id };

    private static readonly string[] ReadOnlyCapability = ["read"];

    [Fact]
    public async Task AskUserShouldReturnTheChosenLabelsAndNameWhatWasLeftOpen()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        fixture.Broker.Answer = request => new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Answered,
            [new UserPromptAnswer("scope", [1], null)]);

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[]
            {
                new { id = "scope", text = "How far?", options = new[] { new { label = "Narrow" }, new { label = "Wide" } } },
                new { id = "tests", text = "Add tests?", options = new[] { new { label = "Yes" }, new { label = "No" } } }
            }
        });

        result.GetProperty("outcome").GetString().ShouldBe("answered");
        var answers = result.GetProperty("answers").EnumerateArray().ToArray();
        // Positions travel over the wire; labels are what the model and the transcript get.
        answers.ShouldHaveSingleItem().GetProperty("selected")[0].GetString().ShouldBe("Wide");
        result.GetProperty("guidance").GetString()!.ShouldContain("tests");
    }

    [Fact]
    public async Task AskUserShouldDropAnAnswerNamingAnOptionThatWasNeverOffered()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        fixture.Broker.Answer = _ => new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Answered,
            [new UserPromptAnswer("scope", [7], null), new UserPromptAnswer("gone", [0], null)]);

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[] { new { id = "scope", text = "How far?", options = new[] { new { label = "Narrow" } } } }
        });

        result.GetProperty("answers").GetArrayLength().ShouldBe(0);
        result.GetProperty("guidance").GetString()!.ShouldContain("state the assumption");
    }

    [Fact]
    public async Task AskUserShouldCarryFreeTextThroughUntouched()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        fixture.Broker.Answer = _ => new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Answered,
            [new UserPromptAnswer("name", [], "  Контрагенты  ")]);

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[] { new { id = "name", text = "What should it be called?", options = Array.Empty<object>() } }
        });

        result.GetProperty("answers")[0].GetProperty("other").GetString().ShouldBe("Контрагенты");
    }

    [Fact]
    public async Task AskUserDeclinedShouldTellTheModelToStopRatherThanChoose()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        fixture.Broker.Answer = _ => new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Declined, []);

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[] { new { id = "scope", text = "How far?", options = new[] { new { label = "Narrow" } } } }
        });

        result.GetProperty("outcome").GetString().ShouldBe("declined");
        result.GetProperty("answers").GetArrayLength().ShouldBe(0);
        var guidance = result.GetProperty("guidance").GetString()!;
        guidance.ShouldContain("Do not proceed");
        guidance.ShouldNotContain("option you judge best");
    }

    [Fact]
    public async Task AskUserShouldReturnSeveralSelectedDirectories()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        fixture.Broker.Answer = _ => new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Answered,
            [new UserPromptAnswer("directories", [], null,
                [@" C:\Projects\One ", @"C:\Projects\Two", @"c:\projects\one"])]);

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[]
            {
                new { id = "directories", text = "Select project directories", options = Array.Empty<object>(), pathKind = "directories" }
            }
        });

        var answer = result.GetProperty("answers")[0];
        answer.GetProperty("paths").EnumerateArray().Select(item => item.GetString())
            .ShouldBe([@"C:\Projects\One", @"C:\Projects\Two"]);
        fixture.Broker.LastRequest!.Questions[0].PathKind.ShouldBe("directories");
    }

    [Theory]
    [InlineData("branch", true)]
    [InlineData("commit", true)]
    [InlineData("commit", false)]
    public async Task AskUserShouldReturnGitValuesInSelectionOrder(string kind, bool multiple)
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        fixture.Broker.Answer = _ => new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Answered,
            [new UserPromptAnswer("git", [], null, Values: [" refs/heads/Fix ", "refs/heads/fix", "refs/heads/Fix"])]);
        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[] { new { id = "git", text = "Choose Git changes", options = Array.Empty<object>(),
                pickerKind = kind, repositoryPath = Path.GetTempPath(), revision = "main", multiSelect = multiple, allowOther = false } }
        });
        result.GetProperty("answers")[0].GetProperty("values").EnumerateArray().Select(item => item.GetString())
            .ShouldBe(multiple ? ["refs/heads/Fix", "refs/heads/fix"] : ["refs/heads/Fix"]);
        var question = fixture.Broker.LastRequest!.Questions[0];
        question.PickerKind.ShouldBe(kind);
        question.RepositoryPath.ShouldBe(Path.GetTempPath());
        question.Revision.ShouldBe("main");
        result.GetProperty("guidance").GetString().ShouldBe("Proceed on these answers.");
    }

    [Theory]
    [InlineData("unknown", null)]
    [InlineData("branch", null)]
    [InlineData("commit", "file")]
    public async Task AskUserShouldRefuseAnInvalidGitPicker(string kind, string? pathKind)
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[] { new { id = "git", text = "Choose", options = Array.Empty<object>(), pickerKind = kind,
                repositoryPath = pathKind is null ? "relative-path" : Path.GetTempPath(), pathKind, allowOther = false } }
        }, expectError: true);
        result.GetProperty("outcome").GetString().ShouldBe("invalid");
        fixture.Broker.LastRequest.ShouldBeNull();
    }

    [Theory]
    // Every limit here is about the card staying readable, and each one is reported in words the
    // model can act on rather than as a schema failure it can only repeat.
    [InlineData("no questions")]
    [InlineData("duplicate ids")]
    [InlineData("unanswerable")]
    public async Task AskUserShouldRefuseAQuestionNobodyCouldRead(string kind)
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        object arguments = kind switch
        {
            "no questions" => new { questions = Array.Empty<object>() },
            "duplicate ids" => new
            {
                questions = new[]
                {
                    new { id = "a", text = "One?", options = new[] { new { label = "Yes" } }, allowOther = true },
                    new { id = "a", text = "Two?", options = new[] { new { label = "Yes" } }, allowOther = true }
                }
            },
            _ => new { questions = new[] { new { id = "a", text = "One?", options = Array.Empty<object>(), allowOther = false } } }
        };

        var result = await AppFixture.CallAsync(session, "ask_user", arguments, expectError: true);
        result.GetProperty("outcome").GetString().ShouldBe("invalid");
        result.GetProperty("error").GetString().ShouldNotBeNullOrWhiteSpace();
        fixture.Broker.LastRequest.ShouldBeNull();
    }

    [Fact]
    public async Task AskUserInABackgroundRunShouldAnswerItselfAndSendTheQuestionUpwards()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync(interactive: false);

        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            questions = new[] { new { id = "scope", text = "How far?", options = new[] { new { label = "Narrow" } } } }
        });

        result.GetProperty("outcome").GetString().ShouldBe("dismissed");
        result.GetProperty("guidance").GetString()!.ShouldContain("final answer");
        // Nobody was asked, rather than asked and timed out.
        fixture.Broker.LastRequest.ShouldBeNull();
    }

    [Fact]
    public async Task ShouldOpenAChatInTheUsersWindowOnlyWhenItExists()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var requests = fixture.Navigation.SubscribeAsync(stop.Token).GetAsyncEnumerator(stop.Token);
        var chat = await fixture.Chats.CreateAsync(fixture.ProjectId, new CreateChatRequest("Next"), CancellationToken.None);

        var missing = await AppFixture.CallAsync(session, "app_navigate",
            new { projectId = fixture.ProjectId, chatId = Guid.NewGuid() }, expectError: true);
        var opening = AppFixture.CallAsync(session, "app_navigate", new { projectId = fixture.ProjectId, chatId = chat.Id });
        (await requests.MoveNextAsync()).ShouldBeTrue();
        var request = requests.Current;
        var clientId = Guid.NewGuid();
        fixture.Navigation.Claim(request.RequestId, clientId).ShouldBeTrue();
        opening.IsCompleted.ShouldBeFalse();
        fixture.Navigation.Complete(request.RequestId, new AppNavigationDecision(clientId, "applied")).ShouldBeTrue();
        var opened = await opening;

        missing.GetProperty("opened").GetBoolean().ShouldBeFalse();
        opened.GetProperty("opened").GetBoolean().ShouldBeTrue();
        opened.GetProperty("effect").GetString().ShouldBe("Applied click to chat 'Next'.");
        // Only the request that passed its checks reaches the window.
        request.ShouldBe(new AppNavigation(fixture.ProjectId, chat.Id, null, fixture.ChatId, "Test", "Next",
            Target: "chat", RequestId: request.RequestId, ExpiresAt: request.ExpiresAt));
        await requests.DisposeAsync();
    }

    [Fact]
    public async Task ShouldNotMoveTheWindowFromABackgroundRun()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync(interactive: false);

        var result = await AppFixture.CallAsync(session, "app_navigate", new { projectId = fixture.ProjectId }, expectError: true);

        result.GetProperty("opened").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task GuideLearningQuestionsShouldWaitWithoutExpiry()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync(interactive: false, isGuide: true);
        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            timeoutSeconds = 0, timeoutBehavior = "cancel",
            questions = new[] { new { id = "next", text = "What next?", allowOther = true,
                options = new[] { new { label = "Models", recommended = true }, new { label = "Finish", recommended = false } } } }
        });
        fixture.Broker.LastRequest.ShouldNotBeNull().Presentation.ShouldBe("overlay");
        fixture.Broker.LastRequest.SubmitDefaults.ShouldBeFalse();
        fixture.Broker.LastTimeout.ShouldBe(Timeout.InfiniteTimeSpan);
        result.GetProperty("outcome").GetString().ShouldBe("answered");
    }

    [Theory]
    [InlineData(false, "cancel")]
    [InlineData(true, "submit_defaults")]
    public async Task UntimedQuestionsShouldBeLimitedToExplicitGuideChoices(bool isGuide, string behavior)
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync(isGuide: isGuide);
        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            presentation = "overlay", timeoutSeconds = 0, timeoutBehavior = behavior,
            questions = new[] { new { id = "next", text = "What next?",
                options = new[] { new { label = "Models", recommended = true } } } }
        }, expectError: true);
        result.GetProperty("outcome").GetString().ShouldBe("invalid");
        fixture.Broker.LastRequest.ShouldBeNull();
    }

    [Fact]
    public async Task OutsideChatQuestionsShouldReachBackgroundRunsAndCancelOnSilence()
    {
        await using var fixture = await AppFixture.CreateAsync();
        fixture.Broker.Answer = _ => new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Expired, []);
        await using var session = await fixture.OpenAsync(interactive: false);
        var result = await AppFixture.CallAsync(session, "ask_user", new
        {
            presentation = "overlay", timeoutSeconds = 10,
            questions = new[] { new { id = "guide", text = "Start a guide?", options = new[] { new { label = "Start" }, new { label = "Not now" } } } }
        });
        fixture.Broker.LastRequest.ShouldNotBeNull().Presentation.ShouldBe("overlay");
        fixture.Broker.LastTimeout.ShouldBe(TimeSpan.FromSeconds(10));
        result.GetProperty("outcome").GetString().ShouldBe("expired");
        result.GetProperty("guidance").GetString()!.ShouldContain("Stop the dependent action");
        result.GetProperty("guidance").GetString()!.ShouldNotContain("Continue with the option");
    }

    [Fact]
    public async Task DefaultCountdownShouldRequireExplicitRecommendationsForEveryQuestion()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        var missing = await AppFixture.CallAsync(session, "ask_user", new
        {
            presentation = "overlay", timeoutBehavior = "submit_defaults",
            questions = new[] { new { id = "mode", text = "Guide mode?", options = new[] { new { label = "Show" } } } }
        }, expectError: true);
        missing.GetProperty("outcome").GetString().ShouldBe("invalid");
        fixture.Broker.LastRequest.ShouldBeNull();
        await AppFixture.CallAsync(session, "ask_user", new
        {
            presentation = "overlay", timeoutBehavior = "submit_defaults", timeoutSeconds = 15,
            questions = new[] { new { id = "mode", text = "Guide mode?", options = new[] { new { label = "Show", recommended = true } } } }
        });
        fixture.Broker.LastRequest.ShouldNotBeNull().SubmitDefaults.ShouldBeTrue();
        fixture.Broker.LastRequest.Questions[0].Options[0].Recommended.ShouldBeTrue();
        fixture.Broker.LastTimeout.ShouldBe(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task ShouldShowSettingsWithoutActivatingTheControlAndReportAStoppedClick()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var requests = fixture.Navigation.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        var showing = AppFixture.CallAsync(session, "app_navigate", new
        {
            target = "settings.guide.enabled", action = "show", comment = "This switch controls guide invitations.", waitForContinue = true,
            timeoutSeconds = 7
        });
        (await requests.MoveNextAsync()).ShouldBeTrue();
        requests.Current.Action.ShouldBe("show");
        requests.Current.ExpiresAt.ShouldNotBeNull().ShouldBeInRange(
            DateTimeOffset.UtcNow.AddSeconds(6), DateTimeOffset.UtcNow.AddSeconds(8));
        requests.Current.WaitForContinue.ShouldBeTrue();
        var owner = Guid.NewGuid();
        fixture.Navigation.Claim(requests.Current.RequestId, owner).ShouldBeTrue();
        fixture.Navigation.Complete(requests.Current.RequestId, new(owner, "applied")).ShouldBeTrue();
        (await showing).GetProperty("opened").GetBoolean().ShouldBeTrue();
        var clicking = AppFixture.CallAsync(session, "app_navigate", new { target = "settings.guide.enabled", action = "click" });
        (await requests.MoveNextAsync()).ShouldBeTrue();
        requests.Current.WaitForContinue.ShouldBeTrue();
        fixture.Navigation.Claim(requests.Current.RequestId, owner).ShouldBeTrue();
        fixture.Navigation.Complete(requests.Current.RequestId, new(owner, "stopped")).ShouldBeTrue();
        var stopped = await clicking;
        stopped.GetProperty("opened").GetBoolean().ShouldBeFalse();
        stopped.GetProperty("outcome").GetString().ShouldBe("stopped");
        stopped.GetProperty("effect").GetString()!.ShouldContain("Stop this guide");
    }

    [Fact]
    public async Task ShouldOpenSettingsForADirectRequestWithoutAContinueStep()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var requests = fixture.Navigation.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        var opening = AppFixture.CallAsync(session, "app_navigate", new { target = "settings", action = "show" });
        (await requests.MoveNextAsync()).ShouldBeTrue();
        requests.Current.Target.ShouldBe("settings");
        requests.Current.WaitForContinue.ShouldBeFalse();
        requests.Current.WaitForUser.ShouldBeFalse();
        requests.Current.Comment.ShouldBeNull();
        var owner = Guid.NewGuid();
        fixture.Navigation.Claim(requests.Current.RequestId, owner).ShouldBeTrue();
        fixture.Navigation.Complete(requests.Current.RequestId, new(owner, "applied")).ShouldBeTrue();
        var result = await opening;
        result.GetProperty("opened").GetBoolean().ShouldBeTrue();
        result.GetProperty("outcome").GetString().ShouldBe("applied");
    }

    [Fact]
    public async Task ShouldTellAGuideToContinueWithAVisibleControlWhenOneIsNotInTheView()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync(isGuide: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var requests = fixture.Navigation.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        var showing = AppFixture.CallAsync(session, "app_navigate", new { target = "chat.fork", action = "show", comment = "Fork here." });
        (await requests.MoveNextAsync()).ShouldBeTrue();
        var owner = Guid.NewGuid();
        fixture.Navigation.Claim(requests.Current.RequestId, owner).ShouldBeTrue();
        fixture.Navigation.Complete(requests.Current.RequestId, new(owner, "unavailable", "The target is not available in this view.")).ShouldBeTrue();
        var result = await showing;
        result.GetProperty("outcome").GetString().ShouldBe("unavailable");
        var effect = result.GetProperty("effect").GetString()!;
        effect.ShouldContain("Do not stop");
        effect.ShouldContain("action='targets'");
        effect.ShouldContain("ask_user");
    }

    [Fact]
    public async Task ShouldNeverNavigateToAGuideServiceChat()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync(isGuide: true);
        var guide = await fixture.Chats.CreateAsync(fixture.ProjectId,
            new CreateChatRequest("Guide · Branches", Kind: ChatKind.Guide.Value), CancellationToken.None);

        var other = await AppFixture.CallAsync(session, "app_navigate",
            new { projectId = fixture.ProjectId, chatId = guide.Id, target = "chat.fork", action = "show" }, expectError: true);
        var own = await AppFixture.CallAsync(session, "app_navigate",
            new { projectId = fixture.ProjectId, chatId = fixture.ChatId, action = "show" }, expectError: true);

        other.GetProperty("opened").GetBoolean().ShouldBeFalse();
        other.GetProperty("error").GetString()!.ShouldContain("hidden service chat");
        own.GetProperty("error").GetString()!.ShouldContain("hidden service chat");
    }

    [Fact]
    public async Task ShouldSetUpADemoChatWithAQuestionAndAnAnswerAndOpenIt()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync(isGuide: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var requests = fixture.Navigation.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        var opening = AppFixture.CallAsync(session, "app_navigate", new { target = "chat.demo", comment = "A chat to practise on." });
        (await requests.MoveNextAsync()).ShouldBeTrue();
        var request = requests.Current;
        request.Target.ShouldBe("chat");
        request.Action.ShouldBe("click");
        request.WaitForContinue.ShouldBeTrue();
        request.Comment.ShouldBe("A chat to practise on.");
        var owner = Guid.NewGuid();
        fixture.Navigation.Claim(request.RequestId, owner).ShouldBeTrue();
        fixture.Navigation.Complete(request.RequestId, new(owner, "applied")).ShouldBeTrue();
        var opened = await opening;

        opened.GetProperty("opened").GetBoolean().ShouldBeTrue();
        var demo = await fixture.Chats.GetAsync(fixture.ProjectId, request.ChatId.ShouldNotBeNull(), CancellationToken.None);
        demo.ShouldNotBeNull();
        demo.Kind.ShouldBe(ChatKind.Demo.Value);
        demo.Messages.Select(message => message.Role).ShouldBe(["User", "Assistant"]);
    }

    [Fact]
    public async Task ShouldSetUpNoDemoChatOutsideAGuide()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();

        var refused = await AppFixture.CallAsync(session, "app_navigate", new { target = "chat.demo" }, expectError: true);

        refused.GetProperty("opened").GetBoolean().ShouldBeFalse();
        (await fixture.Chats.ListAsync(fixture.ProjectId, CancellationToken.None)).ShouldNotContain(chat => chat.Title == GuideChats.DemoTitle);
    }

    [Fact]
    public async Task ShouldPreventActivationInAShowOnlyGuide()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync(isGuide: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var requests = fixture.Navigation.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        var action = AppFixture.CallAsync(session, "app_navigate", new { target = "settings.guide.enabled", action = "click" });
        (await requests.MoveNextAsync()).ShouldBeTrue();
        var request = requests.Current;
        request.Action.ShouldBe("show");
        request.WaitForContinue.ShouldBeTrue();
        var owner = Guid.NewGuid();
        fixture.Navigation.Claim(request.RequestId, owner).ShouldBeTrue();
        fixture.Navigation.Complete(request.RequestId, new(owner, "applied")).ShouldBeTrue();
        (await action).GetProperty("effect").GetString()!.ShouldContain("Applied show");
    }

    [Fact]
    public async Task ShouldDiscoverVisibilityFromTheAttachedWindow()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var requests = fixture.Navigation.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        var action = AppFixture.CallAsync(session, "app_navigate", new { action = "targets" });
        (await requests.MoveNextAsync()).ShouldBeTrue();
        var request = requests.Current;
        request.Action.ShouldBe("targets");
        var owner = Guid.NewGuid();
        fixture.Navigation.Claim(request.RequestId, owner).ShouldBeTrue();
        var targets = new AppNavigationTargets().All.Select(target => target with
        {
            Visible = target.Id == "project", UiLabel = target.Id == "project" ? "Current project" : null,
            UiHint = target.Id == "project" ? "Open project" : null, Enabled = target.Id == "project", State = "aria-expanded=false"
        }).ToArray();
        fixture.Navigation.Complete(request.RequestId, new(owner, "applied", Targets: targets)).ShouldBeTrue();
        var result = await action;
        result.GetProperty("outcome").GetString().ShouldBe("targets");
        result.GetProperty("targets").EnumerateArray().Single(target => target.GetProperty("id").GetString() == "project")
            .GetProperty("visible").GetBoolean().ShouldBeTrue();
        var project = result.GetProperty("targets").EnumerateArray().Single(target => target.GetProperty("id").GetString() == "project");
        project.GetProperty("hint").GetString().ShouldBe(targets.Single(target => target.Id == "project").Hint);
        project.GetProperty("uiLabel").GetString().ShouldBe("Current project");
        project.GetProperty("uiHint").GetString().ShouldBe("Open project");
        project.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        project.GetProperty("state").GetString().ShouldBe("aria-expanded=false");
    }

    [Fact]
    public async Task ShouldKeepServiceGuideChatsOutOfTheNormalChatList()
    {
        await using var fixture = await AppFixture.CreateAsync();
        var guide = await fixture.Chats.CreateAsync(fixture.ProjectId,
            new CreateChatRequest("Guide", Kind: ChatKind.Guide.Value,
                KindState: JsonSerializer.SerializeToElement(new { mode = "click" })), CancellationToken.None);
        (await fixture.Chats.ListAsync(fixture.ProjectId, CancellationToken.None)).ShouldNotContain(chat => chat.Id == guide.Id);
        var restored = await fixture.Chats.GetAsync(fixture.ProjectId, guide.Id, CancellationToken.None);
        restored.ShouldNotBeNull().Kind.ShouldBe(ChatKind.Guide.Value);
        restored.KindState!.Value.GetProperty("mode").GetString().ShouldBe("click");
    }

    [Fact]
    public async Task ShouldArchiveWithoutLosingPinOrHistoryAndRestoreOnANewUserMessage()
    {
        await using var fixture = await AppFixture.CreateAsync();
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        var message = await fixture.Chats.AppendMessageAsync(fixture.ProjectId, fixture.ChatId,
            new(null, null, "User", "archive searchable history", chat!.Revision), CancellationToken.None);
        var pin = await fixture.Chats.PinAsync(fixture.ProjectId, fixture.ChatId,
            new(true, message!.Revision), CancellationToken.None);
        await using var session = await fixture.OpenAsync();
        var operationId = Guid.NewGuid();
        await AppFixture.CallAsync(session, "app_chats", new { operation = "Archive", projectId = fixture.ProjectId,
            operationId, chatId = fixture.ChatId, revision = pin!.Revision });
        var archived = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        archived!.ArchivedAt.ShouldNotBeNull();
        archived.ArchiveOperationId.ShouldBe(operationId);
        archived.Messages.ShouldHaveSingleItem();
        (await fixture.Chats.ListAsync(fixture.ProjectId, CancellationToken.None)).Single().IsPinned.ShouldBeTrue();
        var active = await AppFixture.CallAsync(session, "app_read", new { resource = "Chats", projectId = fixture.ProjectId });
        active.GetProperty("returned").GetInt32().ShouldBe(0);
        var archive = await AppFixture.CallAsync(session, "app_read", new { resource = "Chats", projectId = fixture.ProjectId, archiveScope = "Archived" });
        archive.GetProperty("returned").GetInt32().ShouldBe(1);
        var found = await AppFixture.CallAsync(session, "app_read", new { resource = "Search", query = "searchable", archiveScope = "Archived" });
        found.GetProperty("items")[0].GetProperty("isArchived").GetBoolean().ShouldBeTrue();
        var resumed = await fixture.Chats.AppendMessageAsync(fixture.ProjectId, fixture.ChatId,
            new(null, archived.Messages[0].Id, "User", "continue", archived.Revision), CancellationToken.None);
        resumed!.ArchivedAt.ShouldBeNull();
        resumed.ArchiveOperationId.ShouldBeNull();
        resumed.Messages.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(UserPromptOutcome.Dismissed)]
    [InlineData(UserPromptOutcome.Expired)]
    [InlineData(UserPromptOutcome.Interrupted)]
    [InlineData(UserPromptOutcome.Declined)]
    [InlineData(UserPromptOutcome.Answered)]
    public async Task ShouldNeverArchiveABatchWithoutAnAffirmativeAnswer(UserPromptOutcome outcome)
    {
        await using var fixture = await AppFixture.CreateAsync();
        fixture.Broker.Answer = _ => new UserPromptResponse(Guid.NewGuid(), outcome,
            outcome == UserPromptOutcome.Answered ? [new UserPromptAnswer("archive", [1], null)] : []);
        await using var session = await fixture.OpenAsync();
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        var result = await AppFixture.CallAsync(session, "app_chats", new { operation = "ArchiveBatch", projectId = fixture.ProjectId,
            operationId = Guid.NewGuid(), dryRun = false, targets = new[] { new { chatId = fixture.ChatId, revision = chat!.Revision } } }, expectError: true);
        result.GetProperty("applied").GetBoolean().ShouldBeFalse();
        (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None))!.ArchivedAt.ShouldBeNull();
    }

    [Fact]
    public async Task ShouldPreviewByActivityAndSkipChangesMadeAfterThePreview()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        var cutoff = DateTimeOffset.UtcNow.AddDays(1);
        var preview = await AppFixture.CallAsync(session, "app_chats", new { operation = "ArchiveBatch", projectId = fixture.ProjectId,
            operationId = Guid.NewGuid(), activityBefore = cutoff });
        preview.GetProperty("dryRun").GetBoolean().ShouldBeTrue();
        preview.GetProperty("current").GetProperty("chats").GetArrayLength().ShouldBe(1);
        await fixture.Chats.RenameAsync(fixture.ProjectId, fixture.ChatId, new("Changed after preview", chat!.Revision), CancellationToken.None);
        var result = await AppFixture.CallAsync(session, "app_chats", new { operation = "ArchiveBatch", projectId = fixture.ProjectId,
            operationId = Guid.NewGuid(), dryRun = false, targets = new[] { new { chatId = fixture.ChatId, revision = chat.Revision } } }, expectError: true);
        result.GetProperty("current").GetProperty("skipped").GetArrayLength().ShouldBe(1);
        (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None))!.ArchivedAt.ShouldBeNull();
        fixture.Broker.LastRequest.ShouldNotBeNull();
    }

    [Fact]
    public async Task ShouldExcludePinnedChatsFromPreviewUnlessRequested()
    {
        await using var fixture = await AppFixture.CreateAsync();
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        await fixture.Chats.PinAsync(fixture.ProjectId, fixture.ChatId, new(true, chat!.Revision), CancellationToken.None);
        await using var session = await fixture.OpenAsync();
        var cutoff = DateTimeOffset.UtcNow.AddDays(1);
        var preview = await AppFixture.CallAsync(session, "app_chats", new { operation = "ArchiveBatch", projectId = fixture.ProjectId,
            operationId = Guid.NewGuid(), activityBefore = cutoff });
        preview.GetProperty("current").GetProperty("chats").GetArrayLength().ShouldBe(0);
        var included = await AppFixture.CallAsync(session, "app_chats", new { operation = "ArchiveBatch", projectId = fixture.ProjectId,
            operationId = Guid.NewGuid(), activityBefore = cutoff, includePinned = true });
        included.GetProperty("current").GetProperty("chats").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task ShouldUndoOnlyTheSpecifiedArchiveOperation()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        var original = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        var first = Guid.NewGuid();
        await AppFixture.CallAsync(session, "app_chats", new { operation = "Archive", projectId = fixture.ProjectId,
            operationId = first, chatId = fixture.ChatId, revision = original!.Revision });
        var archived = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        await AppFixture.CallAsync(session, "app_chats", new { operation = "Restore", projectId = fixture.ProjectId,
            operationId = Guid.NewGuid(), chatId = fixture.ChatId, revision = archived!.Revision });
        var restored = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        var second = Guid.NewGuid();
        await AppFixture.CallAsync(session, "app_chats", new { operation = "Archive", projectId = fixture.ProjectId,
            operationId = second, chatId = fixture.ChatId, revision = restored!.Revision });
        await AppFixture.CallAsync(session, "app_chats", new { operation = "UndoArchive", projectId = fixture.ProjectId,
            operationId = Guid.NewGuid(), archiveOperationId = first });
        (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None))!.ArchiveOperationId.ShouldBe(second);
        await AppFixture.CallAsync(session, "app_chats", new { operation = "UndoArchive", projectId = fixture.ProjectId,
            operationId = Guid.NewGuid(), archiveOperationId = second });
        (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None))!.ArchivedAt.ShouldBeNull();
    }

    [Fact]
    public async Task ShouldApplyAConfirmedBatchAndReplayWithoutAnotherConfirmation()
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        var chat = await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None);
        var confirmations = 0;
        fixture.Broker.Answer = _ =>
        {
            confirmations++;
            return new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Answered, [new UserPromptAnswer("archive", [0], null)]);
        };
        var arguments = new { operation = "ArchiveBatch", projectId = fixture.ProjectId, operationId = Guid.NewGuid(),
            dryRun = false, targets = new[] { new { chatId = fixture.ChatId, revision = chat!.Revision } } };
        var applied = await AppFixture.CallAsync(session, "app_chats", arguments);
        applied.GetProperty("applied").GetBoolean().ShouldBeTrue();
        var replay = await AppFixture.CallAsync(session, "app_chats", arguments);
        replay.GetProperty("replayed").GetBoolean().ShouldBeTrue();
        confirmations.ShouldBe(1);
        (await fixture.Chats.GetAsync(fixture.ProjectId, fixture.ChatId, CancellationToken.None))!.ArchiveOperationId.ShouldBe(arguments.operationId);
    }

    [Theory]
    [InlineData(4_096)]
    [InlineData(8_192)]
    [InlineData(16_384)]
    [InlineData(131_072)]
    public async Task ShouldFitActualAppToolSchemasWithAdaptiveInstructions(long window)
    {
        await using var fixture = await AppFixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        var connection = new ConnectionSettings(Guid.NewGuid(), "Test", "https://example.test/v1", "model", true, true, false,
            ContextWindowTokens: window, ReservedOutputTokens: 1_000);
        var preview = await fixture.Standing.BuildAsync(fixture.ProjectId, true, TestContext.Current.CancellationToken, connection);
        var estimator = new ContextTokenEstimator();
        var resolver = new ConnectionContextLimitsResolver();
        var policy = new AdaptiveContextPolicy(estimator, resolver);
        ChatCompletionMessage[] messages = [.. preview.Layers.Where(layer => layer.Content.Length > 0)
            .Select(layer => new ChatCompletionMessage("system", layer.Content)), new("system", "Finish the task with tools and a verified answer."), new("user", "Hello")];
        var selection = policy.Choose(connection, "Hello", messages, session.Tools);
        var plan = new ChatContextPlanner(estimator, new ChatContextCompactor(estimator, new ContextSummaryWriter(new ContextTokenEstimator(), new ToolResultContextProjector(), new AdaptiveContextPolicy(new ContextTokenEstimator(), new AI.Contracts.Settings.ConnectionContextLimitsResolver())), new ToolResultContextProjector()), resolver, policy)
            .Plan(connection, "model", messages, selection.Tools.Select(tool => tool.ModelDefinition).ToArray());
        plan.Fits.ShouldBeTrue();
        selection.SelectedTokens.ShouldBeLessThanOrEqualTo(selection.BudgetTokens);
        selection.Tools.ShouldContain(tool => tool.OriginalName == "tool_search");
    }

    private sealed class AppFixture : IAsyncDisposable
    {
        private readonly AppToolsComposition _composition;

        /// <summary>Stands in for the run waiting on the question, so the tool can be exercised alone.</summary>
        public TestPromptBroker Broker { get; } = new();
        public Mock<IConnectionModelsResolver> Models { get; } = new(MockBehavior.Strict);
        public Mock<IGlobalSecretStore> Secrets => Mock.Get(_composition.Secrets);

        public IProjectService Projects => _composition.Projects;
        public IChatService Chats => _composition.Chats;
        public IStandingInstructions Standing => _composition.Standing;
        public IGlobalSettingsService GlobalSettings => _composition.GlobalSettings;
        private IGlobalSettingsRepository Settings => _composition.Settings;
        private IToolSessionFactory Sessions => _composition.Sessions;
        public Guid ProjectId { get; private set; }
        public Guid ChatId { get; private set; }

        private AppFixture()
        {
            _composition = new AppToolsComposition(
                options: new ServerOptions("data", null, true),
                fileSystem: new MemoryFileSystem(),
                completion: Mock.Of<IChatCompletionClient>(),
                broker: Broker,
                modelsResolver: Models.Object);
        }

        public static async Task<AppFixture> CreateAsync()
        {
            var fixture = new AppFixture();
            await fixture.Settings.SaveAsync(new GlobalSettings(
                [new ConnectionSettings(Guid.NewGuid(), "Test", "https://example.test/v1", "model", true, true, false)],
                [], []), CancellationToken.None);
            fixture.ProjectId = (await fixture.Projects.CreateAsync(new CreateProjectRequest("Test", ""), CancellationToken.None)).Id;
            fixture.ChatId = (await fixture.Chats.CreateAsync(fixture.ProjectId, new CreateChatRequest("Chat"), CancellationToken.None)).Id;
            return fixture;
        }

        public Task<IToolSession> OpenAsync(bool interactive = true, bool isGuide = false) =>
            Sessions.OpenAsync([], AppServerOnly, new ToolRunContext(ProjectId, ChatId, ChatId, interactive,
                    Kind: isGuide ? ChatKind.Guide : ChatKind.Conversation),
                TestContext.Current.CancellationToken);

        /// <summary>Calls a tool the way the agent does, and hands back its structured result.</summary>
        public static async Task<JsonElement> CallAsync(IToolSession session, string name, object arguments, bool expectError = false)
        {
            var tool = session.Tools.Single(item => item.OriginalName == name);
            var result = await session.CallAsync(tool, JsonSerializer.Serialize(arguments), null,
                TestContext.Current.CancellationToken);
            result.IsError.ShouldBe(expectError);
            return result.StructuredContent!.Value;
        }

        public ChangeWatch WatchChanges() => new(_composition.Changes);

        public IAppNavigationSignal Navigation => _composition.Navigation;

        public ValueTask DisposeAsync() => _composition.DisposeAsync();
    }

    /// <summary>
    /// Answers a question however the test wants it answered, and keeps what was asked. The real
    /// broker is the dispatcher, which needs a live run; this one needs nothing, which is what lets
    /// the tool's own behaviour be tested without one.
    /// </summary>
    private sealed class TestPromptBroker : IUserPromptBroker
    {
        public UserPromptRequest? LastRequest { get; private set; }
        public TimeSpan LastTimeout { get; private set; }

        public Func<UserPromptRequest, UserPromptResponse> Answer { get; set; } =
            request => new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Answered,
                request.Questions.Select(question => new UserPromptAnswer(question.Id, [0], null)).ToArray());

        public Task<UserPromptResponse> AskAsync(ToolRunContext run, UserPromptRequest request, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastTimeout = timeout;
            return Task.FromResult(Answer(request));
        }
    }

    /// <summary>Waits for the Host to announce that application data moved.</summary>
    private sealed class ChangeWatch : IDisposable
    {
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(10));
        private readonly Task<bool> _signalled;

        public ChangeWatch(IAppDataChangeSignal signal)
        {
            _signalled = Task.Run(async () =>
            {
                await foreach (var _ in signal.SubscribeAsync(_stop.Token)) return true;
                return false;
            });
        }

        public async Task<bool> WaitAsync()
        {
            try { return await _signalled; }
            catch (OperationCanceledException) { return false; }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _stop.Dispose();
        }
    }
}
