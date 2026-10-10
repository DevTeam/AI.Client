namespace AI.Application.Tests.Skills;

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
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

public class ChatToolRiskAssessSkillTests
{
    private readonly Guid _projectId = Guid.CreateVersion7();
    private readonly Guid _chatId = Guid.CreateVersion7();
    private readonly Guid _connectionId = Guid.CreateVersion7();
    private readonly Guid _questionId = Guid.CreateVersion7();
    private readonly List<ChatCompletionRequest> _requests = [];
    private ToolApprovalMode _mode = ToolApprovalMode.Auto;

    [Fact]
    public async Task ShouldAllowACallTheModelFindsLowRisk()
    {
        var approver = CreateApprover("""{"decision":"allow","risk":"low","reason":"Reads a file in the project."}""");

        var result = await approver.DecideAsync(_projectId, _chatId, _chatId, Tool(), """{"path":"src/a.cs"}""",
            TestContext.Current.CancellationToken);

        result.ShouldBe(new ToolAutoApproval(true, "Reads a file in the project."));
        var input = _requests.ShouldHaveSingleItem().ContextMessages![^1].Content;
        input.ShouldContain("Fix the empty list case");
        input.ShouldContain("C:\\\\work");
        input.ShouldContain("src/a.cs");
        input.ShouldContain("\"readOnly\":true");
    }

    [Theory]
    [InlineData("""{"decision":"ask","risk":"high","reason":"Deletes the repository."}""", "Deletes the repository.")]
    // An allow that does not also call the risk low contradicts itself.
    [InlineData("""{"decision":"allow","risk":"medium","reason":"Writes outside the project."}""", "Writes outside the project.")]
    [InlineData("```json\n{\"decision\":\"ask\",\"risk\":\"medium\",\"reason\":\"Sends mail.\"}\n```", "Sends mail.")]
    public async Task ShouldAskWithTheReasonWhenTheCallIsNotPlainlySafe(string answer, string reason)
    {
        var result = await CreateApprover(answer).DecideAsync(_projectId, _chatId, _chatId, Tool(), "{}",
            TestContext.Current.CancellationToken);

        result.ShouldBe(new ToolAutoApproval(false, reason));
    }

    [Theory]
    [InlineData("Sure, looks safe to me.")]
    [InlineData("""{"decision":"yes"}""")]
    [InlineData("")]
    public async Task ShouldAskWhenTheAssessmentIsUnusable(string answer)
    {
        var result = await CreateApprover(answer).DecideAsync(_projectId, _chatId, _chatId, Tool(), "{}",
            TestContext.Current.CancellationToken);

        result.ShouldBe(ToolAutoApproval.Ask);
    }

    [Fact]
    public async Task ShouldReplaceAChineseReasonForARussianRequest()
    {
        var result = await CreateApprover("""{"decision":"ask","risk":"medium","reason":"请检查脚本参数。"}""",
                "Исправь ошибку в приложении.")
            .DecideAsync(_projectId, _chatId, _chatId, Tool(), "{}", TestContext.Current.CancellationToken);

        result.ShouldBe(new ToolAutoApproval(false, "Review this tool call and its arguments before allowing it."));
    }

    [Fact]
    public async Task ShouldPreserveAChineseReasonForAChineseRequest()
    {
        var result = await CreateApprover("""{"decision":"ask","risk":"medium","reason":"请检查脚本参数。"}""",
                "请检查这个脚本。")
            .DecideAsync(_projectId, _chatId, _chatId, Tool(), "{}", TestContext.Current.CancellationToken);

        result.ShouldBe(new ToolAutoApproval(false, "请检查脚本参数。"));
    }

    [Theory]
    [InlineData(ToolApprovalMode.Ask)]
    [InlineData(ToolApprovalMode.Auto)]
    public async Task ShouldPreserveTheCompleteAssessmentInBothModes(ToolApprovalMode mode)
    {
        _mode = mode;
        var reason = new string('x', 300) + "\nCheck the destination before allowing this call.";
        var answer = JsonSerializer.Serialize(new { decision = "ask", risk = "high", reason });

        var result = await CreateApprover(answer).DecideAsync(_projectId, _chatId, _chatId, Tool(), "{}",
            TestContext.Current.CancellationToken);

        result.ShouldBe(new ToolAutoApproval(false, reason));
    }

    [Theory]
    [InlineData("allow", "low")]
    [InlineData("ask", "high")]
    public async Task ShouldRecommendInManualModeWithoutAllowingTheCall(string decision, string risk)
    {
        _mode = ToolApprovalMode.Ask;

        var result = await CreateApprover($$"""{"decision":"{{decision}}","risk":"{{risk}}","reason":"Review this call."}""")
            .DecideAsync(_projectId, _chatId, _chatId, Tool(), "{}", TestContext.Current.CancellationToken);

        result.ShouldBe(new ToolAutoApproval(false, "Review this call."));
        _requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ShouldAllowFullAccessWithoutAnAssessment()
    {
        _mode = ToolApprovalMode.FullAccess;

        var result = await CreateApprover("""{"decision":"allow","risk":"low","reason":"x"}""")
            .DecideAsync(_projectId, _chatId, _chatId, Tool(), "{}", TestContext.Current.CancellationToken);

        result.Allowed.ShouldBeTrue();
        _requests.ShouldBeEmpty();
    }

    private static AgentTool Tool()
    {
        var schema = JsonSerializer.SerializeToElement(new { type = "object" });
        var descriptor = new ToolDescriptor("mcp_fs__read_file", "read_file", "Read file", "Reads a text file.", schema,
            null, new ToolAnnotations(null, true, false, true, false), [], null);
        return new AgentTool(new ChatToolDefinition("mcp_fs__read_file", "Reads a text file.", schema), descriptor,
            Guid.CreateVersion7(), "read_file", "hash");
    }

    private ToolAutoApprover CreateApprover(string answer, string userRequest = "Fix the empty list case")
    {
        var now = DateTimeOffset.UtcNow;
        var chats = new Mock<IChatService>(MockBehavior.Strict);
        chats.Setup(item => item.GetAsync(_projectId, _chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ChatDetails(_chatId, _projectId, "Fix", now, now, 3, _connectionId,
                [new ChatMessageView(_questionId, null, "User", userRequest, now)],
                [new ChatBranchView(_chatId, _questionId, "Main")], ApprovalMode: _mode));
        var projects = new Mock<IProjectService>(MockBehavior.Strict);
        projects.Setup(item => item.GetAsync(_projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectDetails(_projectId, "Project", "", now, now, 1,
                [new DirectoryGrantSettings(Guid.CreateVersion7(), "work", "C:\\work", true, [])], [], []));
        var settings = new Mock<IGlobalSettingsRepository>(MockBehavior.Strict);
        settings.Setup(item => item.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalSettings([new ConnectionSettings(_connectionId, "Main", "https://example.test/v1",
                "test-model", true, true, false)], [], []));
        var secrets = new Mock<IGlobalSecretStore>(MockBehavior.Strict);
        secrets.Setup(item => item.GetAsync("connection", _connectionId, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        var completion = new Mock<IChatCompletionClient>(MockBehavior.Strict);
        completion.Setup(item => item.StreamAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Returns((ChatCompletionRequest request, CancellationToken token) => Respond(request, answer, token));
        var catalog = new BuiltInSkillCatalog();
        var skill = new ChatToolRiskAssessSkill(catalog, chats.Object, projects.Object, settings.Object, secrets.Object,
            completion.Object, NullLogger<ChatToolRiskAssessSkill>.Instance);
        return new ToolAutoApprover(chats.Object, new SkillRunner(catalog, null!, chatToolRiskAssessSkill: skill),
            new ChatBranchSettingsResolver());
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
