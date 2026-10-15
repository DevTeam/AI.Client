namespace AI.Application.Tests.Tools.Commands;

using System.Text.Json;
using AI.Application.Projects;
using AI.Application.Tools.Commands;
using AI.Contracts.Projects;
using AI.Contracts.Settings;
using Moq;
using Shouldly;
using Xunit;

public sealed class PathRulesTests
{
    private static readonly string Project = TestPrograms.Project;
    private readonly Guid _projectId = Guid.CreateVersion7();
    private readonly Guid _chatId = Guid.CreateVersion7();
    private readonly CommandRuleService _service;
    private IReadOnlyList<CommandRule> _rules = [];

    public PathRulesTests()
    {
        var now = DateTimeOffset.UtcNow;
        var projects = new Mock<IProjectService>();
        projects.Setup(service => service.GetAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new ProjectDetails(_projectId, "Project", "", now, now, 0,
                [new DirectoryGrantSettings(Guid.CreateVersion7(), "project", Project, true, ["write"])], [], []));
        var repository = new Mock<ICommandRuleRepository>();
        repository.Setup(item => item.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _rules);
        repository.Setup(item => item.UpdateAsync(It.IsAny<Func<IReadOnlyList<CommandRule>, IReadOnlyList<CommandRule>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Func<IReadOnlyList<CommandRule>, IReadOnlyList<CommandRule>> update, CancellationToken _) => _rules = update(_rules));
        var ids = new Mock<IIdGenerator>();
        ids.Setup(item => item.Create()).Returns(Guid.CreateVersion7);
        var clock = new Mock<IClock>();
        clock.Setup(item => item.UtcNow).Returns(() => DateTimeOffset.UtcNow);
        _service = new CommandRuleService(repository.Object, projects.Object,
            new ToolRuleProfiles(TestPrograms.Parser(), TestPrograms.Paths), ids.Object, clock.Object);
    }

    [Fact]
    public void ShouldOfferRulesForTheToolsTheWebListsAndNoOthers()
    {
        new ToolRuleProfiles(TestPrograms.Parser(), TestPrograms.Paths).All.Select(profile => profile.Tool)
            .ShouldBe(CommandRule.Tools);
    }

    [Theory]
    [InlineData("docs/**", "docs/**")]
    [InlineData("./docs/**", "docs/**")]
    [InlineData(@"src\app\*.cs", "src/app/*.cs")]
    [InlineData("  docs//guide.md ", "docs/guide.md")]
    public void ShouldSpellAPathPatternOneWay(string text, string canonical)
    {
        PathPattern.TryParse(text, TestPrograms.Paths, out var pattern, out _).ShouldBeTrue();
        pattern!.Text.ShouldBe(canonical);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../outside/**")]
    [InlineData("docs/../secret")]
    [InlineData("./")]
    public void ShouldRefuseAPatternThatLeavesTheProject(string text)
    {
        PathPattern.TryParse(text, TestPrograms.Paths, out var pattern, out var problem).ShouldBeFalse();
        pattern.ShouldBeNull();
        problem.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("docs/**", "docs/a.md", "Allow")]
    [InlineData("docs/**", "docs/guide/deep/b.md", "Allow")]
    [InlineData("src/*.cs", "src/a.cs", "Allow")]
    [InlineData("src/*.cs", "src/nested/a.cs", null)]
    [InlineData("**", "anything/at/all.txt", "Allow")]
    public async Task ShouldMatchPathsBelowTheProject(string pattern, string path, string? decision)
    {
        await AddAsync("write_file", pattern, "Allow");

        (await DecideAsync("write_file", new { path = Full(path), content = "x" })).ShouldBe(decision);
    }

    [Fact]
    public async Task ShouldNeedEveryPathForAnAllowAndAnyForADeny()
    {
        await AddAsync("move_file", "src/**", "Allow");
        (await DecideAsync("move_file", new { source = Full("src/a.cs"), destination = Full("src/b.cs") })).ShouldBe("Allow");
        (await DecideAsync("move_file", new { source = Full("src/a.cs"), destination = Full("docs/a.cs") })).ShouldBeNull();

        await AddAsync("move_file", "docs/**", "Deny");
        (await DecideAsync("move_file", new { source = Full("src/a.cs"), destination = Full("docs/a.cs") })).ShouldBe("Deny");
    }

    [Theory]
    [InlineData(".env")]
    [InlineData("config/.env.local")]
    [InlineData(".git/hooks/pre-commit")]
    [InlineData("certs/server.pem")]
    [InlineData(".ssh/id_ed25519")]
    public async Task ShouldAskBeforeChangingSecretsOrTheRepositoryItself(string path)
    {
        await AddAsync("write_file", "**", "Allow");

        var evaluation = await EvaluateAsync("write_file", new { path = Full(path), content = "x" });

        evaluation.Decision.ShouldBe("Ask");
        evaluation.Subject.BlocksAllow.ShouldBeTrue();
        evaluation.Subject.Warnings.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task ShouldAskBeforeChangingAPathOutsideTheProject()
    {
        await AddAsync("write_file", "**", "Allow");
        var outside = Path.Combine(TestPrograms.Root, "elsewhere", "a.txt");

        var evaluation = await EvaluateAsync("write_file", new { path = outside, content = "x" });

        evaluation.Decision.ShouldBeNull();
        evaluation.Subject.BlocksAllow.ShouldBeTrue();
        evaluation.Subject.Options.ShouldBeEmpty();
        evaluation.Subject.Display.ShouldBe($"write_file {outside}");
    }

    [Fact]
    public async Task ShouldDeleteOnlyByAnExactPath()
    {
        await AddAsync("delete_file", "docs/**", "Allow");
        (await DecideAsync("delete_file", new { path = Full("docs/old.md") })).ShouldBeNull();

        await AddAsync("delete_file", "docs/old.md", "Allow");
        (await DecideAsync("delete_file", new { path = Full("docs/old.md") })).ShouldBe("Allow");
        (await EvaluateAsync("delete_file", new { path = Full("docs/old.md") })).Subject.Options.ShouldBe(["docs/old.md"]);
    }

    [Fact]
    public async Task ShouldOfferTheFolderTheExactPathAndTheWholeProject()
    {
        var evaluation = await EvaluateAsync("write_file", new { path = Full("src/app/a.cs"), content = "x" });

        evaluation.Subject.Display.ShouldBe("write_file src/app/a.cs");
        evaluation.Subject.Options.ShouldBe(["src/app/**", "src/app/a.cs", "**"]);
        (await EvaluateAsync("move_file", new { source = Full("src/a/x.cs"), destination = Full("src/b/x.cs") }))
            .Subject.Options.ShouldBe(["src/**", "**"]);
    }

    [Fact]
    public async Task ShouldKeepRulesOfOneToolToThatTool()
    {
        await AddAsync("edit_file", "**", "Allow");

        (await DecideAsync("write_file", new { path = Full("a.txt"), content = "x" })).ShouldBeNull();
        (await DecideAsync("edit_file", new { path = Full("a.txt"), edits = Array.Empty<object>() })).ShouldBe("Allow");
    }

    private Task<CommandRule> AddAsync(string tool, string pattern, string decision) =>
        _service.AddAsync(new CreateCommandRuleRequest(CommandRule.Project, _projectId, null, pattern, decision, tool),
            CancellationToken.None);

    private async Task<string?> DecideAsync(string tool, object arguments) => (await EvaluateAsync(tool, arguments)).Decision;

    private async Task<CommandEvaluation> EvaluateAsync(string tool, object arguments) =>
        (await _service.EvaluateAsync(_projectId, _chatId, tool, JsonSerializer.Serialize(arguments), CancellationToken.None))
        .ShouldNotBeNull();

    private static string Full(string relative) => Path.Combine([Project, .. relative.Split('/')]);
}
