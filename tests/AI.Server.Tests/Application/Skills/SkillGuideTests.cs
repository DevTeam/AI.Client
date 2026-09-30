namespace AI.Application.Tests.Skills;

using AI.Application.Chat;
using AI.Application.Skills;
using AI.Contracts.Chat;
using AI.Contracts.Skills;
using Moq;
using Shouldly;
using Xunit;

public class SkillGuideTests
{
    private static readonly Guid ProjectId = Guid.CreateVersion7();
    private readonly SkillGuide _guide = new(new BuiltInSkillCatalog());

    [Fact]
    public async Task ShouldNameThePlaybookTheConversationIsIn()
    {
        var active = await _guide.ActivePlaybookAsync(ProjectId,
        [
            User("Add a CSV export"),
            RunSkill("1", "code-feature-implement"),
            Result("1", "Completed"),
            new ChatCompletionMessage("assistant", "Done."),
            User("Also add a header row")
        ], TestContext.Current.CancellationToken);

        active.ShouldNotBeNull().Id.ShouldBe("code-feature-implement");
    }

    [Fact]
    public async Task ShouldTakeTheLatestPlaybookAndSkipOneThatFailedToLoad()
    {
        var token = TestContext.Current.CancellationToken;

        (await _guide.ActivePlaybookAsync(ProjectId,
        [
            User("Run the tests"),
            RunSkill("1", "code-tests-run"),
            Result("1", "Completed"),
            User("Fix them"),
            RunSkill("2", "code-bug-fix"),
            Result("2", "Completed")
        ], token)).ShouldNotBeNull().Id.ShouldBe("code-bug-fix");
        (await _guide.ActivePlaybookAsync(ProjectId,
        [
            User("Run the tests"),
            RunSkill("1", "code-tests-run"),
            Result("1", "Completed"),
            RunSkill("2", "code-bug-fix"),
            Result("2", "Failed")
        ], token)).ShouldNotBeNull().Id.ShouldBe("code-tests-run");
    }

    [Fact]
    public async Task ShouldForgetAPlaybookSeveralTurnsBackAndOneThatIsNotAPlaybook()
    {
        var token = TestContext.Current.CancellationToken;
        var history = new List<ChatCompletionMessage> { User("Save that I use tabs"), RunSkill("1", "memory-save"), Result("1", "Completed") };
        for (var turn = 0; turn < SkillGuide.ActiveTurns; turn++)
            history.AddRange([User("Next question"), new ChatCompletionMessage("assistant", "Answer.")]);

        (await _guide.ActivePlaybookAsync(ProjectId, history, token)).ShouldBeNull();
        (await _guide.ActivePlaybookAsync(ProjectId,
            [User("Rename the chat"), RunSkill("1", "chat-rename"), Result("1", "Completed")], token)).ShouldBeNull();
    }

    [Fact]
    public async Task ShouldPreferAProjectSkillAndLeaveDisabledOnesOut()
    {
        var builtIn = new BuiltInSkillCatalog();
        var summary = builtIn.GetById("chat-summary")!;
        var catalog = new Mock<ISkillCatalog>();
        catalog.Setup(item => item.ListAsync(ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            .. builtIn.List(),
            summary with { Source = "Project", Description = "Project summary." },
            builtIn.GetById("memory-save")! with { Source = "User", Enabled = false }
        ]);

        var effective = await new SkillGuide(catalog.Object).EffectiveAsync(ProjectId, TestContext.Current.CancellationToken);

        effective.Single(skill => skill.Id == "chat-summary").Description.ShouldBe("Project summary.");
        effective.ShouldNotContain(skill => skill.Id == "memory-save");
        effective.Select(skill => skill.Id).ShouldBe(effective.Select(skill => skill.Id).Order(StringComparer.Ordinal));
    }

    private static ChatCompletionMessage User(string text) => new("user", text);

    private static ChatCompletionMessage RunSkill(string id, string skillId) =>
        new("assistant", string.Empty, [new ChatToolCall(id, "mcp_app__run_skill", $"{{\"skillId\":\"{skillId}\",\"parameters\":{{}}}}")]);

    private static ChatCompletionMessage Result(string id, string status) =>
        new("tool", $"{{\"skillId\":\"x\",\"status\":\"{status}\"}}", ToolCallId: id);
}
