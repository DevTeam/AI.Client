namespace AI.Application.Tests.Skills;

using System.Runtime.CompilerServices;
using System.Text.Json;
using AI.Application.Chat;
using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Settings;
using AI.Application.Skills;
using AI.Application.Tools;
using AI.Application.Tools.Commands;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Settings;
using AI.Contracts.Skills;
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
    private IReadOnlyList<ChatMessageView>? _messages;
    private IReadOnlyList<string> _playbookTools = ["read_file"];
    private CommandEvaluation? _evaluation;

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

    [Theory]
    [InlineData("read_file", true)]
    [InlineData("mcp_fs__read_file", true)]
    [InlineData("process_run", false)]
    public async Task ShouldPassTheActivePlaybookWithTheRequestsItContinues(string allowed, bool listed)
    {
        _playbookTools = [allowed];
        _messages = SkillHistory("""{"status":"Completed","skillId":"release-notes"}""", "Yes, go on.");

        await CreateApprover("""{"decision":"allow","risk":"low","reason":"x"}""")
            .DecideAsync(_projectId, _chatId, _chatId, Tool(), "{}", TestContext.Current.CancellationToken);

        using var input = Input();
        var root = input.RootElement;
        root.GetProperty("user_request").GetString().ShouldBe("Yes, go on.");
        root.GetProperty("earlier_user_requests").EnumerateArray().Select(item => item.GetString())
            .ShouldBe(["Write the release notes for 2.1."]);
        var skill = root.GetProperty("active_skill");
        skill.GetProperty("id").GetString().ShouldBe("release-notes");
        skill.GetProperty("tool_is_allowed").GetBoolean().ShouldBe(listed);
        skill.GetProperty("instructions").GetString().ShouldNotBeNull().ShouldContain("Read the changelog");
    }

    [Theory]
    // A load the runner refused gave the assistant no instructions to follow.
    [InlineData("release-notes", """{"status":"Failed","error":"Unavailable"}""")]
    // An id the catalog does not know brings no text with it, whatever the call claimed.
    [InlineData("trusted-deploy", """{"status":"Completed"}""")]
    public async Task ShouldNotPassAPlaybookTheCatalogDidNotLoad(string skillId, string result)
    {
        _messages = SkillHistory(result, "Yes, go on.", skillId);

        await CreateApprover("""{"decision":"ask","risk":"medium","reason":"x"}""")
            .DecideAsync(_projectId, _chatId, _chatId, Tool(), """{"skillId":"release-notes"}""",
                TestContext.Current.CancellationToken);

        using var input = Input();
        input.RootElement.TryGetProperty("active_skill", out _).ShouldBeFalse();
        input.RootElement.GetProperty("user_request").GetString().ShouldBe("Yes, go on.");
    }

    [Fact]
    public async Task ShouldPassOnlyTheLatestRequests()
    {
        var now = DateTimeOffset.UtcNow;
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.CreateVersion7()).ToArray();
        _messages = ids.Select((id, index) => new ChatMessageView(id, index == 0 ? null : ids[index - 1],
            index % 2 == 0 ? "User" : "Assistant", $"Message {index}", now)).ToArray();

        await CreateApprover("""{"decision":"ask","risk":"medium","reason":"x"}""")
            .DecideAsync(_projectId, _chatId, _chatId, Tool(), "{}", TestContext.Current.CancellationToken);

        using var input = Input();
        input.RootElement.GetProperty("user_request").GetString().ShouldBe("Message 4");
        input.RootElement.GetProperty("earlier_user_requests").EnumerateArray().Select(item => item.GetString())
            .ShouldBe(["Message 0", "Message 2"]);
    }

    [Fact]
    public async Task ShouldPassTheParsedCommandOfAProcessRunCall()
    {
        _evaluation = Evaluation(null);

        await CreateApprover("""{"decision":"ask","risk":"high","reason":"Force push."}""")
            .DecideAsync(_projectId, _chatId, _chatId, ProcessTool(), "{}", TestContext.Current.CancellationToken);

        using var input = Input();
        var command = input.RootElement.GetProperty("command");
        command.GetProperty("display").GetString().ShouldBe("git push --force origin main");
        command.GetProperty("destructive").GetBoolean().ShouldBeTrue();
        command.GetProperty("warnings").EnumerateArray().ShouldHaveSingleItem().GetString()
            .ShouldBe("It can discard work or rewrite history.");
        command.GetProperty("rule").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task ShouldNotAllowACommandARuleAsksAbout()
    {
        _evaluation = Evaluation("Ask");

        var result = await CreateApprover("""{"decision":"allow","risk":"low","reason":"Pushes the branch."}""")
            .DecideAsync(_projectId, _chatId, _chatId, ProcessTool(), "{}", TestContext.Current.CancellationToken);

        result.ShouldBe(new ToolAutoApproval(false, "Pushes the branch."));
        using var input = Input();
        input.RootElement.GetProperty("command").GetProperty("rule").GetString().ShouldBe("Ask git push * (project)");
    }

    [Fact]
    public async Task ShouldNotAskTheCommandRulesAboutAnotherTool()
    {
        _evaluation = Evaluation("Ask");

        var result = await CreateApprover("""{"decision":"allow","risk":"low","reason":"Reads a file."}""")
            .DecideAsync(_projectId, _chatId, _chatId, Tool(), "{}", TestContext.Current.CancellationToken);

        result.Allowed.ShouldBeTrue();
        using var input = Input();
        input.RootElement.TryGetProperty("command", out _).ShouldBeFalse();
    }

    private static ChatMessageView[] SkillHistory(string result, string followUp, string skillId = "release-notes")
    {
        var now = DateTimeOffset.UtcNow;
        var (request, call, output, question, answer) =
            (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        return
        [
            new(request, null, "User", "Write the release notes for 2.1.", now),
            new(call, request, "Assistant", "", now,
                ToolCalls: [new("call-1", "mcp_app__run_skill", $$"""{"skillId":"{{skillId}}"}""")]),
            new(output, call, "Tool", result, now, ToolCallId: "call-1"),
            new(question, output, "Assistant", "Shall I read the changelog?", now),
            new(answer, question, "User", followUp, now)
        ];
    }

    private JsonDocument Input() =>
        JsonDocument.Parse(_requests.ShouldHaveSingleItem().ContextMessages![^1].Content);

    private SkillDefinition Playbook() =>
        new("release-notes", "Release notes", "Writes release notes from the changelog.", "Project",
            "---\nid: release-notes\n---\n\n1. Read the changelog.\n2. Write docs/release.md.",
            true, JsonSerializer.SerializeToElement(new { type = "object" }), AllowedTools: _playbookTools,
            Kind: SkillKinds.Playbook);

    private ISkillCatalog Catalog()
    {
        var builtIn = new BuiltInSkillCatalog();
        IReadOnlyList<SkillDefinition> All() => [.. builtIn.List(), Playbook()];
        var catalog = new Mock<ISkillCatalog>();
        catalog.Setup(item => item.List()).Returns(All);
        catalog.Setup(item => item.GetById(It.IsAny<string>()))
            .Returns((string id) => All().FirstOrDefault(skill => skill.Id == id));
        catalog.Setup(item => item.ListAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(All);
        catalog.Setup(item => item.GetByIdAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, Guid? _, CancellationToken _) => All().FirstOrDefault(skill => skill.Id == id));
        return catalog.Object;
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
        IReadOnlyList<ChatMessageView> messages = _messages ?? [new ChatMessageView(_questionId, null, "User", userRequest, now)];
        var chats = new Mock<IChatService>(MockBehavior.Strict);
        chats.Setup(item => item.GetAsync(_projectId, _chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ChatDetails(_chatId, _projectId, "Fix", now, now, 3, _connectionId, messages,
                [new ChatBranchView(_chatId, messages[^1].Id, "Main")], ApprovalMode: _mode));
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
        var catalog = Catalog();
        var skill = new ChatToolRiskAssessSkill(catalog, new SkillGuide(catalog), chats.Object, projects.Object, settings.Object, secrets.Object,
            completion.Object, NullLogger<ChatToolRiskAssessSkill>.Instance);
        var commands = new Mock<ICommandRuleService>();
        commands.Setup(item => item.EvaluateAsync(_projectId, _chatId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _evaluation);
        return new ToolAutoApprover(chats.Object, new SkillRunner(catalog, null!, chatToolRiskAssessSkill: skill),
            new ChatBranchSettingsResolver(), commands.Object);
    }

    private static AgentTool ProcessTool()
    {
        var schema = JsonSerializer.SerializeToElement(new { type = "object" });
        var descriptor = new ToolDescriptor("mcp_built_in__process_run", "process_run", "Run process", "Runs a program.",
            schema, null, new ToolAnnotations(null, false, true, false, true), [], null);
        return new AgentTool(new ChatToolDefinition("mcp_built_in__process_run", "Runs a program.", schema), descriptor,
            DefaultMcpServer.Id, "process_run", "hash");
    }

    private static CommandEvaluation Evaluation(string? ruleDecision)
    {
        var command = new ParsedCommand("git", @"C:\Git\git.exe", true, [], ["push", "--force", "origin", "main"],
            @"C:\work", [], false, [], true);
        var rule = ruleDecision is null ? null : new CommandRule(Guid.CreateVersion7(), CommandRule.Project, null, null,
            "git push *", ruleDecision, DateTimeOffset.UtcNow);
        return new CommandEvaluation(new CommandCheck(command, ["It can discard work or rewrite history."], false, true),
            rule, ruleDecision, ["git push --force origin main"]);
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
