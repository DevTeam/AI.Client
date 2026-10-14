namespace AI.Web.Tests.Widgets;

using System.Text.Json;
using AI.Contracts.Chat;
using AI.Contracts.Chats;
using AI.Contracts.Resources;
using AI.Contracts.Runs;
using AI.Contracts.Skills;
using AI.Contracts.Tools;
using AI.Web.Widgets;
using Shouldly;
using Xunit;

public class ChatSkillStatisticsCalculatorTests
{
    private const string RunSkill = "mcp_app__run_skill";
    private readonly ChatSkillStatisticsCalculator _calculator = new(new ToolResultCodec(new ToolResultModelProjector()));

    [Fact]
    public void ShouldCountSlashSkillsAndAssistantRunsPerSkill()
    {
        var stats = Calculate([User("review"), Calls(Run("a", "review"), Run("b", "commit")),
            Result("a", "Completed", isError: false), Result("b", "Failed", isError: true),
            User(), Calls(Run("c", "review")), Result("c", "Completed", isError: false)]);

        (stats.Uses, stats.Requested, stats.ByAssistant, stats.Turns, stats.TurnsWithSkills).ShouldBe((4, 1, 3, 2, 2));
        stats.Skills.Select(skill => (skill.Id, skill.Uses, skill.Requested, skill.ByAssistant, skill.Failed))
            .ShouldBe([("review", 3, 1, 2, 0), ("commit", 1, 0, 1, 1)]);
        stats.Count(ChatSkillOutcome.Succeeded).ShouldBe(2);
        stats.Count(ChatSkillOutcome.Failed).ShouldBe(1);
    }

    [Fact]
    public void ShouldNameSkillsFromTheCatalogThenFromTheMessage()
    {
        var stats = Calculate([User("review", "Stored name"), Calls(Run("a", "commit"), Run("b", "unknown"))],
            skills: [Skill("commit", "Commit changes", "git-commit", SkillKinds.Playbook)]);

        var byId = stats.Skills.ToDictionary(skill => skill.Id);
        byId["review"].Name.ShouldBe("Stored name");
        byId["commit"].Name.ShouldBe("Commit changes");
        byId["commit"].Icon.ShouldBe("git-commit");
        byId["commit"].Kind.ShouldBe(SkillKinds.Playbook);
        byId["unknown"].Name.ShouldBe("unknown");
        byId["unknown"].Kind.ShouldBeNull();
    }

    [Fact]
    public void ShouldReadSkippedFromTheBodyAndFallBackToTheErrorFlag()
    {
        var stats = Calculate([User(), Calls(Run("a", "x"), Run("b", "x"), Run("c", "x"), Run("d", "x")),
            Result("a", "Skipped", isError: false),
            Result("b", "Cancelled", isError: false),
            Result("c", "Completed", isError: false) with { Content = "", ContentOmitted = true, ToolResultIsError = false },
            Result("d", "Completed", isError: false) with { IsIncomplete = true }]);

        stats.Count(ChatSkillOutcome.Skipped).ShouldBe(1);
        stats.Count(ChatSkillOutcome.Failed).ShouldBe(1);
        stats.Count(ChatSkillOutcome.Succeeded).ShouldBe(1);
        stats.Count(ChatSkillOutcome.Unknown).ShouldBe(1);
    }

    [Fact]
    public void ShouldCountSearchesApartAndIgnoreOtherTools()
    {
        var stats = Calculate([User(), Calls(new ChatToolCall("s", "mcp_app__skill_search", "{\"query\":\"git\"}"),
            new("r", "mcp_built_in__read_text_file", "{}"), new("x", "mcp_other__run_skill", "{\"skillId\":\"x\"}"),
            new("m", RunSkill, "{\"skillId\":"))]);

        stats.Searches.ShouldBe(1);
        stats.Uses.ShouldBe(0);
        stats.TurnsWithSkills.ShouldBe(0);
    }

    [Fact]
    public void ShouldMergeLiveRunsOnceAndKeepThemToTheLastTurn()
    {
        var user = User();
        var run = Snapshot(user) with { ActiveTools = [new ActiveToolInvocation("a", RunSkill, Arguments("x"), DateTimeOffset.UnixEpoch)] };
        var stats = Calculate([User("x"), user, Calls(Run("a", "x"), Run("b", "x"))], run, scope: ChatWidgetScope.LastTurn);

        stats.Uses.ShouldBe(2);
        stats.IsRunning.ShouldBeTrue();
        stats.Count(ChatSkillOutcome.Running).ShouldBe(1);
        stats.Count(ChatSkillOutcome.AwaitingResult).ShouldBe(1);
        stats.Skills.Single().IsRunning.ShouldBeTrue();
    }

    [Fact]
    public void ShouldPointEachSkillAtItsNewestUse()
    {
        var first = User("x");
        var second = Calls(Run("a", "x"));
        var stats = Calculate([first, second]);

        stats.Skills.Single().LatestMessageId.ShouldBe(second.Id);
    }

    private ChatSkillStatistics Calculate(IReadOnlyList<ChatMessageView> messages, ChatRunSnapshot? run = null,
        IReadOnlyList<SkillDefinition>? skills = null, ChatWidgetScope scope = ChatWidgetScope.Chat) =>
        _calculator.Calculate(messages, run, skills ?? [], scope);

    private static ChatMessageView User(string? skillId = null, string? name = null) =>
        new(Guid.NewGuid(), null, "User", "Hi", DateTimeOffset.UnixEpoch,
            Resources: skillId is null ? null : [new ChatResource(Guid.NewGuid(), ChatResourceKind.Skill, skillId, name)]);

    private static ChatMessageView Calls(params ChatToolCall[] calls) =>
        new(Guid.NewGuid(), null, "Assistant", "", DateTimeOffset.UnixEpoch, ToolCalls: calls);

    private static ChatToolCall Run(string id, string skillId) => new(id, RunSkill, Arguments(skillId));

    private static string Arguments(string skillId) => JsonSerializer.Serialize(new { skillId, parameters = new { } });

    private static ChatMessageView Result(string id, string status, bool isError) =>
        new(Guid.NewGuid(), null, "Tool", JsonSerializer.Serialize(new { isError, structuredContent = new { status } }),
            DateTimeOffset.UnixEpoch, ToolCallId: id);

    private static SkillDefinition Skill(string id, string name, string icon, string kind) =>
        new(id, name, "", "BuiltIn", "", true, JsonDocument.Parse("{}").RootElement, Kind: kind, Icon: icon);

    private static ChatRunSnapshot Snapshot(ChatMessageView user) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        ChatRunStatus.Generating, "", [], false, null, 1, ActiveMessageId: user.Id);
}
