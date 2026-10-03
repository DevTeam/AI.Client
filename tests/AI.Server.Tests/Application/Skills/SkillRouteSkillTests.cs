namespace AI.Application.Tests.Skills;

using System.Runtime.CompilerServices;
using System.Text.Json;
using AI.Application.Chat;
using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Settings;
using AI.Application.Skills;
using AI.Application.Tools;
using AI.Contracts.Chat;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

public class SkillRouteSkillTests
{
    private readonly Guid _projectId = Guid.CreateVersion7();
    private readonly Guid _chatId = Guid.CreateVersion7();
    private readonly Guid _connectionId = Guid.CreateVersion7();
    private readonly List<ChatCompletionRequest> _requests = [];

    [Fact]
    public async Task ShouldRouteToKnownSkillsAndToolsOnly()
    {
        var routing = CreateRouting("```json\n{\"skills\":[\"project-create\",\"made-up\",\"code-feature-implement\",\"code-bug-fix\"],"
                                    + "\"tools\":[\"mcp_built_in__process_run\",\"mcp_x__nothing\"]}\n```");

        var route = await routing.RouteAsync(Run, [new ChatCompletionMessage("user", "Создай проект с .NET приложением")],
            [Tool("mcp_built_in__process_run", "Run a program and wait for completion. No implicit shell.")],
            TestContext.Current.CancellationToken);

        route.ShouldNotBeNull();
        // An id the catalog does not have is dropped, and at most two skills survive.
        route.Skills.Select(skill => skill.Id).ShouldBe(["project-create", "code-feature-implement"]);
        route.Tools.ShouldBe(["mcp_built_in__process_run"]);
        route.ContinuesActive.ShouldBeFalse();
        var input = JsonDocument.Parse(_requests.Single().ContextMessages![^1].Content).RootElement;
        input.GetProperty("message").GetString().ShouldBe("Создай проект с .NET приложением");
        input.GetProperty("tools")[0].GetString().ShouldBe("mcp_built_in__process_run: Run a program and wait for completion.");
        input.GetProperty("skills").EnumerateArray().Select(item => item.GetString()!)
            .ShouldContain(line => line.StartsWith("project-create: ", StringComparison.Ordinal));
        _requests.Single().Tools.ShouldBeNull();
    }

    [Fact]
    public async Task ShouldSayWhenTheMessageContinuesTheActivePlaybook()
    {
        var routing = CreateRouting("{\"skills\":[\"code-feature-implement\"],\"tools\":[]}");

        var route = await routing.RouteAsync(Run,
        [
            new ChatCompletionMessage("user", "Add a CSV export"),
            new ChatCompletionMessage("assistant", string.Empty,
                [new ChatToolCall("1", "mcp_app__run_skill", "{\"skillId\":\"code-feature-implement\"}")]),
            new ChatCompletionMessage("tool", "{\"status\":\"Completed\"}", ToolCallId: "1"),
            new ChatCompletionMessage("assistant", "Done. Should the file have a header row?"),
            new ChatCompletionMessage("user", "Yes")
        ], [], TestContext.Current.CancellationToken);

        route.ShouldNotBeNull().ContinuesActive.ShouldBeTrue();
        var input = JsonDocument.Parse(_requests.Single().ContextMessages![^1].Content).RootElement;
        input.GetProperty("active_skill").GetString().ShouldBe("code-feature-implement");
        input.GetProperty("previous").GetString().ShouldBe("Done. Should the file have a header row?");
    }

    [Theory]
    [InlineData("I cannot decide.")]
    [InlineData("{\"skills\":\"project-create\"}")]
    public async Task ShouldRouteNowhereOnAnAnswerItCannotRead(string answer)
    {
        var route = await CreateRouting(answer).RouteAsync(Run, [new ChatCompletionMessage("user", "Hi")], [],
            TestContext.Current.CancellationToken);

        route.ShouldNotBeNull().Skills.ShouldBeEmpty();
        route.Tools.ShouldBeEmpty();
    }

    [Fact]
    public async Task ShouldNotRouteAMessageWhoseSkillTheUserPickedOrATurnThatResumes()
    {
        var routing = CreateRouting("{\"skills\":[\"chat-summary\"]}");
        var token = TestContext.Current.CancellationToken;

        (await routing.RouteAsync(Run, [new ChatCompletionMessage("user",
            "The user invoked the skill /chat-summary (Chat summary) for this message. Run it now.")], [], token)).ShouldBeNull();
        (await routing.RouteAsync(Run, [new ChatCompletionMessage("user", "Go"),
            new ChatCompletionMessage("tool", "{}", ToolCallId: "1")], [], token)).ShouldBeNull();
        _requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ShouldNotAskTheModelWhenRoutingIsSwitchedOff()
    {
        var route = await CreateRouting("{\"skills\":[\"chat-summary\"]}", routeSkills: false).RouteAsync(Run,
            [new ChatCompletionMessage("user", "Summarize")], [], TestContext.Current.CancellationToken);

        route.ShouldBeNull();
        _requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ShouldSkipAnOversizedRoutingCatalogueOnASmallConnection()
    {
        var route = await CreateRouting("{}", window: 4_096).RouteAsync(Run,
            [new ChatCompletionMessage("user", "Summarize")], [], TestContext.Current.CancellationToken);
        route.ShouldBeNull();
        _requests.ShouldBeEmpty();
    }

    private ToolRunContext Run => new(_projectId, _chatId, _chatId, true);

    private static AgentTool Tool(string name, string description) =>
        new(new ChatToolDefinition(name, description, JsonDocument.Parse("{}").RootElement), null!, Guid.Empty, name, "");

    private SkillRouting CreateRouting(string answer, bool routeSkills = true, long? window = null)
    {
        var now = DateTimeOffset.UtcNow;
        var chats = new Mock<IChatService>(MockBehavior.Strict);
        chats.Setup(item => item.GetAsync(_projectId, _chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatDetails(_chatId, _projectId, "Chat", now, now, 1, _connectionId, []));
        var projects = new Mock<IProjectService>(MockBehavior.Strict);
        projects.Setup(item => item.GetAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync((ProjectDetails?)null);
        var settings = new Mock<IGlobalSettingsRepository>(MockBehavior.Strict);
        settings.Setup(item => item.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalSettings([new ConnectionSettings(_connectionId, "Main", "https://example.test/v1",
                "test-model", true, true, false, ContextWindowTokens: window)], [], []) { ChatAutomation = new ChatAutomationSettings(RouteSkills: routeSkills) });
        var secrets = new Mock<IGlobalSecretStore>(MockBehavior.Strict);
        secrets.Setup(item => item.GetAsync("connection", _connectionId, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        var completion = new Mock<IChatCompletionClient>(MockBehavior.Strict);
        completion.Setup(item => item.StreamAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Returns((ChatCompletionRequest request, CancellationToken token) => Respond(request, answer, token));
        var catalog = new BuiltInSkillCatalog();
        var guide = new SkillGuide(catalog);
        var estimator = new ContextTokenEstimator();
        var limits = new ConnectionContextLimitsResolver();
        var skill = new SkillRouteSkill(catalog, guide, chats.Object, projects.Object, settings.Object, secrets.Object,
            completion.Object, new ChatContextPlanner(estimator, new ChatContextCompactor(estimator, new ContextSummaryWriter(new ContextTokenEstimator(), new ToolResultContextProjector()), new ToolResultContextProjector()),
                limits, new AdaptiveContextPolicy(estimator, limits)), NullLogger<SkillRouteSkill>.Instance);
        return new SkillRouting(new SkillRunner(catalog, null!, skillRouteSkill: skill), guide, settings.Object);
    }

    private async IAsyncEnumerable<ChatCompletionChunk> Respond(ChatCompletionRequest request, string answer,
        [EnumeratorCancellation] CancellationToken token)
    {
        await Task.Yield();
        token.ThrowIfCancellationRequested();
        lock (_requests) _requests.Add(request);
        yield return new ChatCompletionChunk(answer);
    }
}
