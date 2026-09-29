namespace AI.Application.Tests.Skills;

using System.Text.Json;
using AI.Application.Chat;
using AI.Application.Chats;
using AI.Application.Notifications;
using AI.Application.Projects;
using AI.Application.Settings;
using AI.Application.Skills;
using AI.Contracts.Skills;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

public class SkillMarkdownTests
{
    private static readonly string Playbook = string.Join('\n',
        "---",
        "id: project-describe-short",
        "name: Project describe short",
        "kind: playbook",
        "description: Describe the project in one line; changes nothing.",
        """parameters: {"type":"object","properties":{"focus":{"type":"string"}},"additionalProperties":false}""",
        """tools: ["app_read"]""",
        "---",
        "",
        "1. Read the project with `app_read`.");

    [Fact]
    public void ShouldDefaultToGenericAndReadPlaybooks()
    {
        var generic = SkillMarkdown.Parse(Playbook.Replace("kind: playbook\n", "").Replace("tools: [\"app_read\"]\n", ""), "User");
        var playbook = SkillMarkdown.Parse(Playbook, "User");

        generic.Kind.ShouldBe(SkillKinds.Generic);
        playbook.Kind.ShouldBe(SkillKinds.Playbook);
        playbook.AllowedTools.ShouldBe(["app_read"]);
        SkillMarkdown.Body(playbook.Content).ShouldBe("1. Read the project with `app_read`.");
    }

    [Theory]
    [InlineData("kind: playbook", "kind: script")]
    [InlineData("kind: playbook", "kind: executor")]
    [InlineData("tools: [\"app_read\"]", "tools: [\"app_read\"]\nresult: {\"type\":\"object\"}")]
    [InlineData("tools: [\"app_read\"]", "tools: [\"App-Read\"]")]
    [InlineData("kind: playbook\n", "")]
    public void ShouldRejectInconsistentKinds(string from, string to)
    {
        Should.Throw<ArgumentException>(() => SkillMarkdown.Parse(Playbook.Replace(from, to), "User"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\"\"")]
    [InlineData("\"{\\\"focus\\\":\\\"tests\\\"}\"")]
    public async Task ShouldTreatMissingOrStringParametersAsTheObject(string? raw)
    {
        var projectId = Guid.CreateVersion7();
        var parameters = raw is null ? default : JsonSerializer.Deserialize<JsonElement>(raw);

        var result = await Runner(projectId).RunAsync(
            new SkillInvocation("project-describe-short", projectId, parameters), CancellationToken.None);

        result.Status.ShouldBe("Completed", result.Message);
        result.Output!.Value.GetProperty("arguments").ValueKind.ShouldBe(JsonValueKind.Object);
    }

    [Fact]
    public async Task ShouldShowTheSchemaWhenParametersDoNotMatch()
    {
        var projectId = Guid.CreateVersion7();

        var result = await Runner(projectId).RunAsync(new SkillInvocation("project-describe-short", projectId,
            JsonSerializer.SerializeToElement(new { unknown = 1 })), CancellationToken.None);

        result.Status.ShouldBe("Failed");
        result.Message.ShouldContain("\"focus\"");
    }

    private static SkillRunner Runner(Guid projectId)
    {
        var catalog = new Mock<ISkillCatalog>();
        catalog.Setup(item => item.GetByIdAsync("project-describe-short", projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SkillMarkdown.Parse(Playbook, "User"));
        return new SkillRunner(catalog.Object, new ChatRenameSkill(new BuiltInSkillCatalog(),
            Mock.Of<IChatService>(), Mock.Of<IProjectService>(), Mock.Of<IGlobalSettingsRepository>(),
            Mock.Of<IGlobalSecretStore>(), Mock.Of<IChatCompletionClient>(), Mock.Of<IAppDataChangeSignal>(),
            NullLogger<ChatRenameSkill>.Instance));
    }

    [Fact]
    public async Task ShouldHandPlaybookInstructionsAndContextToTheCaller()
    {
        var projectId = Guid.CreateVersion7();
        var chatId = Guid.CreateVersion7();
        var branchId = Guid.CreateVersion7();
        var catalog = new Mock<ISkillCatalog>();
        catalog.Setup(item => item.GetByIdAsync("project-describe-short", projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SkillMarkdown.Parse(Playbook, "User"));
        var runner = new SkillRunner(catalog.Object, new ChatRenameSkill(new BuiltInSkillCatalog(),
            Mock.Of<IChatService>(), Mock.Of<IProjectService>(), Mock.Of<IGlobalSettingsRepository>(),
            Mock.Of<IGlobalSecretStore>(), Mock.Of<IChatCompletionClient>(), Mock.Of<IAppDataChangeSignal>(),
            NullLogger<ChatRenameSkill>.Instance));

        var result = await runner.RunAsync(new SkillInvocation("project-describe-short", projectId,
            JsonSerializer.SerializeToElement(new { focus = "tests" }), chatId, branchId), CancellationToken.None);

        result.Status.ShouldBe("Completed");
        var output = result.Output!.Value;
        output.GetProperty("kind").GetString().ShouldBe("playbook");
        output.GetProperty("instructions").GetString()!.ShouldStartWith("1. Read the project");
        output.GetProperty("arguments").GetProperty("focus").GetString().ShouldBe("tests");
        output.GetProperty("context").GetProperty("chatId").GetGuid().ShouldBe(chatId);
        output.GetProperty("context").GetProperty("branchId").GetGuid().ShouldBe(branchId);
    }
}
