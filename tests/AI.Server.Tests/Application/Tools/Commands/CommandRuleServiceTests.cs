namespace AI.Application.Tests.Tools.Commands;

using System.Text.Json;
using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Settings;
using AI.Application.Tools;
using AI.Application.Tools.Commands;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Settings;
using Moq;
using Shouldly;
using Xunit;

public sealed class CommandRuleServiceTests
{
    private readonly Guid _projectId = Guid.CreateVersion7();
    private readonly Guid _chatId = Guid.CreateVersion7();
    private readonly Repository _repository = new();
    private readonly CommandRuleService _service;
    private ProjectDetails _project;
    private ChatDetails _chat;
    private GlobalSettings _global = new([], [DefaultMcpServer.Settings], []);

    public CommandRuleServiceTests()
    {
        var now = DateTimeOffset.UtcNow;
        _project = new ProjectDetails(_projectId, "Project", "", now, now, 0,
            [new DirectoryGrantSettings(Guid.CreateVersion7(), "project", TestPrograms.Project, true, [])], [], []);
        _chat = new ChatDetails(_chatId, _projectId, "Chat", now, now, 0, null, []);
        var projects = new Mock<IProjectService>();
        projects.Setup(service => service.GetAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _project);
        var ids = new Mock<IIdGenerator>();
        ids.Setup(item => item.Create()).Returns(Guid.CreateVersion7);
        var clock = new Mock<IClock>();
        clock.Setup(item => item.UtcNow).Returns(() => DateTimeOffset.UtcNow);
        _service = new CommandRuleService(_repository, projects.Object,
            new ToolRuleProfiles(TestPrograms.Parser(), TestPrograms.Paths), ids.Object, clock.Object, Temporary());
    }

    [Fact]
    public async Task ShouldLetTheMostSpecificRuleDecideAndDenyWinAlways()
    {
        await AddAsync(CommandRule.Global, "git *", "Allow");
        await AddAsync(CommandRule.Project, "git push *", "Ask");
        await AddAsync(CommandRule.Global, "git push origin main", "Allow");

        (await DecideAsync("status")).ShouldBe("Allow");
        (await DecideAsync("push", "origin", "topic")).ShouldBe("Ask");
        (await DecideAsync("push", "origin", "main")).ShouldBe("Allow");

        await AddAsync(CommandRule.Global, "git push *", "Deny");
        (await DecideAsync("push", "origin", "main")).ShouldBe("Deny");
    }

    [Fact]
    public async Task ShouldPreferTheNarrowerScopeBetweenEquallySpecificRules()
    {
        await AddAsync(CommandRule.Global, "git log *", "Ask");
        await AddAsync(CommandRule.Project, "git log *", "Allow");
        (await DecideAsync("log", "-1")).ShouldBe("Allow");

        await AddAsync(CommandRule.Chat, "git log *", "Ask");
        (await DecideAsync("log", "-1")).ShouldBe("Ask");
    }

    [Fact]
    public async Task ShouldIgnoreRulesOfOtherProjectsAndChats()
    {
        await _service.AddAsync(new CreateCommandRuleRequest(CommandRule.Project, Guid.CreateVersion7(), null, "git *", "Allow"),
            CancellationToken.None);
        await _service.AddAsync(new CreateCommandRuleRequest(CommandRule.Chat, _projectId, Guid.CreateVersion7(), "git *", "Allow"),
            CancellationToken.None);

        (await DecideAsync("status")).ShouldBeNull();
    }

    [Fact]
    public async Task ShouldAskInsteadOfAllowingWhatTheGuardStops()
    {
        await AddAsync(CommandRule.Global, "git *", "Allow");

        (await DecideAsync("-c", "alias.status=!calc", "status")).ShouldBe("Ask");
        (await DecideAsync("diff", "--output=" + Path.Combine(TestPrograms.Root, "elsewhere"))).ShouldBe("Ask");
        (await EvaluateAsync(TestPrograms.LocalGit, ["status"])).Decision.ShouldBe("Ask");
    }

    [Fact]
    public async Task ShouldAllowAShellOrADestructiveCommandOnlyWordForWord()
    {
        await AddAsync(CommandRule.Global, "git *", "Allow");
        await AddAsync(CommandRule.Global, "pwsh *", "Allow");

        (await DecideAsync("push", "--force", "origin", "main")).ShouldBeNull();
        (await EvaluateAsync("pwsh", ["-NoProfile", "-File", "check.ps1"])).Decision.ShouldBeNull();

        await AddAsync(CommandRule.Global, "pwsh -NoProfile -File check.ps1", "Allow");
        (await EvaluateAsync("pwsh", ["-NoProfile", "-File", "check.ps1"])).Decision.ShouldBe("Allow");
        await AddAsync(CommandRule.Global, "git push -f origin main", "Deny");
        (await DecideAsync("push", "-f", "origin", "main")).ShouldBeNull();
        await AddAsync(CommandRule.Global, "git push --force *", "Deny");
        (await DecideAsync("push", "origin", "+main")).ShouldBe("Deny");
    }

    [Fact]
    public async Task ShouldReplaceARuleForTheSamePatternAndRemoveIt()
    {
        await AddAsync(CommandRule.Project, "git   status", "Allow");
        var replaced = await AddAsync(CommandRule.Project, "git status", "Deny");

        var rules = await _service.ListAsync(_projectId, _chatId, CancellationToken.None);
        rules.ShouldHaveSingleItem().ShouldBe(replaced);
        replaced.Pattern.ShouldBe("git status");

        (await _service.RemoveAsync(replaced.Id, CancellationToken.None)).ShouldBeTrue();
        (await _service.ListAsync(_projectId, _chatId, CancellationToken.None)).ShouldBeEmpty();
        (await _service.RemoveAsync(replaced.Id, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task ShouldRefuseARuleForAToolCommandRulesDoNotDecide()
    {
        await Should.ThrowAsync<ArgumentException>(() => _service.AddAsync(
            new CreateCommandRuleRequest(CommandRule.Global, null, null, "git *", "Allow", "cs_run"), CancellationToken.None));
    }

    [Fact]
    public void ShouldReadARuleSavedBeforeRulesNamedTheirToolAsAProcessRunRule()
    {
        var rule = JsonSerializer.Deserialize<CommandRule>(
            """{"Id":"01a13ecf-386a-75e6-8d82-810fb2d2e4ac","Scope":"Global","Pattern":"git *","Decision":"Allow","CreatedAt":"2026-10-15T00:00:00Z"}""");

        rule.ShouldNotBeNull().Tool.ShouldBe(CommandRule.ProcessRun);
    }

    [Theory]
    [InlineData(CommandRule.Global, "git *", "Maybe")]
    [InlineData(CommandRule.Global, "* status", "Allow")]
    [InlineData(CommandRule.Chat, "git *", "Allow")]
    [InlineData("Branch", "git *", "Allow")]
    public async Task ShouldRefuseAMalformedRule(string scope, string pattern, string decision)
    {
        await Should.ThrowAsync<ArgumentException>(() => _service.AddAsync(
            new CreateCommandRuleRequest(scope, _projectId, null, pattern, decision), CancellationToken.None));
    }

    [Fact]
    public async Task ShouldRefineTheToolPolicyOnlyInsideIt()
    {
        var resolver = Resolver();
        var arguments = Arguments("git", ["status"]);

        async Task<string> ResolveAsync() => (await resolver.ResolveAsync(_projectId, _chatId, _chatId, DefaultMcpServer.Id,
            ToolPolicyResolver.ProcessRun, "hash", arguments, CancellationToken.None)).Decision;

        (await ResolveAsync()).ShouldBe("Ask");
        await AddAsync(CommandRule.Project, "git status", "Allow");
        (await ResolveAsync()).ShouldBe("Allow");
        _chat = _chat with { ToolPolicies = [new ToolPolicySettings(DefaultMcpServer.Id, ToolPolicyResolver.ProcessRun, "hash", "Deny", null, null)] };
        (await ResolveAsync()).ShouldBe("Deny");
        _chat = _chat with { ToolPolicies = [new ToolPolicySettings(DefaultMcpServer.Id, ToolPolicyResolver.ProcessRun, "hash", "Allow", null, null)] };
        await AddAsync(CommandRule.Chat, "git status", "Ask");
        (await ResolveAsync()).ShouldBe("Ask");
        var policy = await resolver.ResolveAsync(_projectId, _chatId, _chatId, DefaultMcpServer.Id,
            ToolPolicyResolver.ProcessRun, "hash", arguments, CancellationToken.None);
        ((ParsedCommand)policy.Command.ShouldNotBeNull().Subject.Value).ExecutablePath.ShouldBe(TestPrograms.Git);
        (await resolver.ResolveAsync(_projectId, _chatId, _chatId, DefaultMcpServer.Id, "read_file", "hash", arguments,
            CancellationToken.None)).Command.ShouldBeNull();
    }

    private ToolPolicyResolver Resolver()
    {
        var projects = new Mock<IProjectService>();
        projects.Setup(service => service.GetAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _project);
        var chats = new Mock<IChatService>();
        chats.Setup(service => service.GetAsync(_projectId, _chatId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _chat);
        var settings = new Mock<IGlobalSettingsRepository>();
        settings.Setup(repository => repository.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _global);
        return new ToolPolicyResolver(projects.Object, chats.Object, settings.Object, new ToolDefaultDecision(),
            new ChatBranchSettingsResolver(), _service);
    }

    private Task<CommandRule> AddAsync(string scope, string pattern, string decision) =>
        _service.AddAsync(new CreateCommandRuleRequest(scope, scope == CommandRule.Global ? null : _projectId,
            scope == CommandRule.Chat ? _chatId : null, pattern, decision), CancellationToken.None);

    private async Task<string?> DecideAsync(params string[] arguments) => (await EvaluateAsync("git", arguments)).Decision;

    private async Task<CommandEvaluation> EvaluateAsync(string executable, string[] arguments) =>
        (await _service.EvaluateAsync(_projectId, _chatId, CommandRule.ProcessRun, Arguments(executable, arguments), CancellationToken.None))
        .ShouldNotBeNull();

    private static IChatTemporaryDirectory Temporary()
    {
        var temporary = new Mock<IChatTemporaryDirectory>();
        temporary.Setup(item => item.ChatRoot(It.IsAny<Guid>(), It.IsAny<Guid>()))
            .Returns((Guid projectId, Guid chatId) => Path.Combine(TestPrograms.Root, "scratch", chatId.ToString("N")));
        return temporary.Object;
    }

    private static string Arguments(string executable, string[] arguments) =>
        JsonSerializer.Serialize(new { executable, arguments, workingDirectory = TestPrograms.Project });

    private sealed class Repository : ICommandRuleRepository
    {
        private IReadOnlyList<CommandRule> _rules = [];

        public Task<IReadOnlyList<CommandRule>> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(_rules);

        public Task<IReadOnlyList<CommandRule>> UpdateAsync(Func<IReadOnlyList<CommandRule>, IReadOnlyList<CommandRule>> update,
            CancellationToken cancellationToken) => Task.FromResult(_rules = update(_rules));
    }
}
