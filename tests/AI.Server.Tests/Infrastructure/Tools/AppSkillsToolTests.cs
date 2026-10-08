namespace AI.Infrastructure.Tests.Tools;

using System.Runtime.CompilerServices;
using System.Text.Json;
using AI.Application.Chat;
using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Settings;
using AI.Application.Skills;
using AI.Application.Tools;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Settings;
using AI.Infrastructure.Storage;
using AI.Infrastructure.Tests.Storage;
using AI.Infrastructure.Tools;
using AI.Server.Hosting;
using Moq;
using Shouldly;
using Xunit;

[Trait("Category", "Integration")]
public sealed class AppSkillsToolTests
{
    private const string CustomSkill = """
        ---
        id: short-summary
        name: Short summary
        description: Produce a brief summary.
        parameters: {"type":"object","properties":{},"additionalProperties":false}
        result: {"type":"string"}
        tools: []
        ---

        Return a JSON string with a short summary.
        """;

    [Fact]
    public async Task ShouldDiscoverAndInvokeSkillThroughAppMcpSession()
    {
        await using var composition = new SubtaskComposition(
            new ServerOptions("data", null, true), new MemoryFileSystem(),
            Mock.Of<IChatCompletionClient>(), Mock.Of<IToolSessionFactory>());
        var projects = composition.Projects;
        var chats = composition.Chats;
        var projectId = (await projects.CreateAsync(new CreateProjectRequest("Test", ""), CancellationToken.None)).Id;
        var chatId = (await chats.CreateAsync(projectId, new CreateChatRequest("Test"), CancellationToken.None)).Id;
        var factory = composition.Sessions;
        await using var session = await factory.OpenAsync([], new ToolRunContext(projectId, chatId, chatId, true),
            TestContext.Current.CancellationToken);
        var search = session.Tools.Single(tool => tool.OriginalName == "skill_search");
        var run = session.Tools.Single(tool => tool.OriginalName == "run_skill");

        search.ModelDefinition.Name.ShouldBe("mcp_app__skill_search");
        run.ModelDefinition.Name.ShouldBe("mcp_app__run_skill");
        run.ModelDefinition.Description.ShouldContain(search.ModelDefinition.Name);
        search.ModelDefinition.Description.ShouldContain(run.ModelDefinition.Name);

        var found = await session.CallAsync(search, """{"query":"rename chat"}""", null,
            TestContext.Current.CancellationToken);
        found.IsError.ShouldBeFalse();
        found.StructuredContent!.Value.GetProperty("skills")[0].GetProperty("id").GetString().ShouldBe("chat-rename");

        var arguments = JsonSerializer.Serialize(new
        {
            skillId = "chat-rename",
            parameters = new { chat_id = "current", mode = "requested", unexpected = true }
        });
        var rejected = await session.CallAsync(run, arguments, null, TestContext.Current.CancellationToken);
        rejected.IsError.ShouldBeTrue();
        rejected.StructuredContent!.Value.GetProperty("status").GetString().ShouldBe("Failed");

        // A playbook whose parameters are all optional runs without any, and says where it runs.
        var playbook = await session.CallAsync(run, """{"skillId":"chat-summary"}""", null,
            TestContext.Current.CancellationToken);
        playbook.IsError.ShouldBeFalse();
        var output = playbook.StructuredContent!.Value.GetProperty("output");
        output.GetProperty("kind").GetString().ShouldBe("playbook");
        output.GetProperty("context").GetProperty("chatId").GetGuid().ShouldBe(chatId);
    }

    [Fact]
    public async Task ShouldShowAvailableSkillsWhenAQueryHasNoMatches()
    {
        await using var composition = new SubtaskComposition(
            new ServerOptions("data", null, true), new MemoryFileSystem(),
            Mock.Of<IChatCompletionClient>(), Mock.Of<IToolSessionFactory>());
        var projectId = (await composition.Projects
            .CreateAsync(new CreateProjectRequest("Test", ""), CancellationToken.None)).Id;
        var chatId = (await composition.Chats
            .CreateAsync(projectId, new CreateChatRequest("Test"), CancellationToken.None)).Id;
        await using var session = await composition.Sessions.OpenAsync([],
            new ToolRunContext(projectId, chatId, chatId, true), TestContext.Current.CancellationToken);
        var search = session.Tools.Single(tool => tool.OriginalName == "skill_search");

        var result = await session.CallAsync(search, """{"query":"тест запуск скилов"}""", null,
            TestContext.Current.CancellationToken);

        result.IsError.ShouldBeFalse();
        var content = result.StructuredContent!.Value;
        content.GetProperty("matchedQuery").GetBoolean().ShouldBeFalse();
        content.GetProperty("guidance").GetString()!.ShouldContain("No skill matched");
        content.GetProperty("skills").EnumerateArray()
            .ShouldContain(skill => skill.GetProperty("id").GetString() == "chat-branch-cleanup");
    }

    [Fact]
    public async Task ShouldSaveFindAndRunProjectSkillThroughAppMcpSession()
    {
        await using var composition = new SubtaskComposition(new ServerOptions("data", null, true),
            new MemoryFileSystem(), new StaticCompletion(), Mock.Of<IToolSessionFactory>());
        var settings = composition.Settings;
        var connectionId = Guid.CreateVersion7();
        await settings.SaveAsync(new GlobalSettings([new ConnectionSettings(connectionId, "Test",
            "https://example.test/v1", "model", true, true, false)], [], []), CancellationToken.None);
        var projectId = (await composition.Projects
            .CreateAsync(new CreateProjectRequest("Test", ""), CancellationToken.None)).Id;
        var chatId = (await composition.Chats
            .CreateAsync(projectId, new CreateChatRequest("Test"), CancellationToken.None)).Id;
        await using var session = await composition.Sessions.OpenAsync([],
            new ToolRunContext(projectId, chatId, chatId, true), TestContext.Current.CancellationToken);
        var manage = session.Tools.Single(tool => tool.OriginalName == "app_skills");
        var search = session.Tools.Single(tool => tool.OriginalName == "skill_search");
        var run = session.Tools.Single(tool => tool.OriginalName == "run_skill");

        var saved = await session.CallAsync(manage, JsonSerializer.Serialize(new
        {
            operation = "Save", operationId = Guid.CreateVersion7(), scope = "Project",
            content = CustomSkill, revision = 0
        }), null, TestContext.Current.CancellationToken);
        saved.IsError.ShouldBeFalse(saved.StructuredContent?.GetRawText());
        saved.StructuredContent!.Value.GetProperty("applied").GetBoolean().ShouldBeTrue();

        var found = await session.CallAsync(search, """{"query":"summary"}""", null,
            TestContext.Current.CancellationToken);
        found.StructuredContent!.Value.GetProperty("skills").EnumerateArray()
            .ShouldContain(item => item.GetProperty("id").GetString() == "short-summary");

        var executed = await session.CallAsync(run,
            """{"skillId":"short-summary","parameters":{}}""", null, TestContext.Current.CancellationToken);
        executed.IsError.ShouldBeFalse();
        executed.StructuredContent!.Value.GetProperty("status").GetString().ShouldBe("Completed");
        executed.StructuredContent.Value.GetProperty("output").GetString().ShouldBe("done");
    }

    [Theory]
    [InlineData("parameters", "{\"path\":{\"type\":\"string\"}}")]
    [InlineData("result", "{\"type\":\"unknown-type\"}")]
    public async Task ShouldRejectInvalidSchemaAndAllowCorrectedSave(string field, string schema)
    {
        await using var composition = new SubtaskComposition(new ServerOptions("data", null, true),
            new MemoryFileSystem(), Mock.Of<IChatCompletionClient>(), Mock.Of<IToolSessionFactory>());
        await using var session = await composition.Sessions.OpenAsync([],
            new ToolRunContext(Guid.Empty, Guid.Empty, Guid.Empty, true), TestContext.Current.CancellationToken);
        var manage = session.Tools.Single(tool => tool.OriginalName == "app_skills");
        var operationId = Guid.CreateVersion7();
        var original = field == "parameters"
            ? "parameters: {\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"
            : "result: {\"type\":\"string\"}";
        var rejected = await session.CallAsync(manage, JsonSerializer.Serialize(new
        {
            operation = "Save", operationId, scope = "User",
            content = CustomSkill.Replace(original, $"{field}: {schema}"), revision = 0
        }), null, TestContext.Current.CancellationToken);

        rejected.IsError.ShouldBeTrue();
        var error = rejected.StructuredContent!.Value;
        error.GetProperty("applied").GetBoolean().ShouldBeFalse();
        error.GetProperty("error").GetString()!.ShouldContain($"SKILL.md {field}");
        error.GetProperty("error").GetString()!.ShouldContain("properties");
        var catalog = composition.Skills;
        (await catalog.GetByIdAsync("short-summary", null, TestContext.Current.CancellationToken)).ShouldBeNull();

        var corrected = await session.CallAsync(manage, JsonSerializer.Serialize(new
        {
            operation = "Save", operationId, scope = "User", content = CustomSkill, revision = 0
        }), null, TestContext.Current.CancellationToken);
        corrected.IsError.ShouldBeFalse();
        corrected.StructuredContent!.Value.GetProperty("applied").GetBoolean().ShouldBeTrue();
        corrected.StructuredContent.Value.GetProperty("replayed").GetBoolean().ShouldBeFalse();
        (await catalog.GetByIdAsync("short-summary", null, TestContext.Current.CancellationToken))!.Revision.ShouldBe(1);
    }

    private sealed class StaticCompletion : IChatCompletionClient
    {
        public Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(ChatCompletionRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatCompletionChunk("\"done\"");
        }
    }
}
