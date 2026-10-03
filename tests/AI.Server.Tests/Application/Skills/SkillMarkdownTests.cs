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

    [Fact]
    public void ShouldReadAliases()
    {
        SkillMarkdown.Parse(Playbook, "User").Aliases.ShouldBe([]);
        SkillMarkdown.Parse(Playbook.Replace("kind: playbook\n", "kind: playbook\naliases: [\"compact\", \"sq\"]\n"), "User")
            .Aliases.ShouldBe(["compact", "sq"]);
    }

    [Fact]
    public void ShouldAllowSharpAliasesWithoutChangingIdSyntax()
    {
        SkillMarkdown.Parse(Playbook.Replace("kind: playbook\n", "kind: playbook\naliases: [\"c#\", \"cs\", \"csharp\"]\n"), "User")
            .Aliases.ShouldBe(["c#", "cs", "csharp"]);
        Should.Throw<ArgumentException>(() =>
            SkillMarkdown.Parse(Playbook.Replace("id: project-describe-short", "id: c#"), "User"));
    }

    [Fact]
    public void ShouldReadIcon()
    {
        SkillMarkdown.Parse(Playbook, "User").Icon.ShouldBeNull();
        SkillMarkdown.Parse(Playbook.Replace("kind: playbook\n", "icon: rocket\nkind: playbook\n"), "User")
            .Icon.ShouldBe("rocket");
        SkillMarkdown.Parse(Playbook.Replace("kind: playbook\n", "icon: M12 3 3 8l9 5 9-5ZM3 13l9 5 9-5\nkind: playbook\n"), "User")
            .Icon.ShouldBe("M12 3 3 8l9 5 9-5ZM3 13l9 5 9-5");
    }

    [Theory]
    [InlineData("Rocket")]
    [InlineData("rocket-ship")]
    [InlineData("🚀")]
    [InlineData("12 3 3 8Z")]
    [InlineData("M12 3\" onload=\"alert(1)")]
    [InlineData("M12 3<script>")]
    public void ShouldRejectUnknownIcons(string icon) =>
        Should.Throw<ArgumentException>(() =>
            SkillMarkdown.Parse(Playbook.Replace("kind: playbook\n", $"icon: {icon}\nkind: playbook\n"), "User"))
            .Message.ShouldContain("rocket");

    [Theory]
    [InlineData("[\"Compact\"]")]
    [InlineData("[\"compact\", \"compact\"]")]
    [InlineData("[\"-compact\"]")]
    [InlineData("[\"compact now\"]")]
    [InlineData("[\"#\"]")]
    [InlineData("[\"c##\"]")]
    [InlineData("[\"c#script\"]")]
    public void ShouldRejectInvalidAliases(string aliases) =>
        Should.Throw<ArgumentException>(() =>
            SkillMarkdown.Parse(Playbook.Replace("kind: playbook\n", $"kind: playbook\naliases: {aliases}\n"), "User"));

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

    [Fact]
    public async Task ShouldTakeQuotedScalarsAsTheDeclaredType()
    {
        var result = await new SkillRunner(new BuiltInSkillCatalog(), BuiltInRenameSkill()).RunAsync(new SkillInvocation(
            "chat-context-compact", Guid.CreateVersion7(),
            JsonSerializer.SerializeToElement(new { action = "compact", target_tokens = "1500" })), CancellationToken.None);

        result.Status.ShouldBe("Completed", result.Message);
        var arguments = result.Output!.Value.GetProperty("arguments");
        arguments.GetProperty("target_tokens").GetInt32().ShouldBe(1500);
        arguments.GetProperty("action").GetString().ShouldBe("compact");
    }

    [Fact]
    public async Task ShouldNameTheArgumentThatFailsTheSchema()
    {
        var result = await new SkillRunner(new BuiltInSkillCatalog(), BuiltInRenameSkill()).RunAsync(new SkillInvocation(
            "chat-context-compact", Guid.CreateVersion7(),
            JsonSerializer.SerializeToElement(new { target_tokens = "many" })), CancellationToken.None);

        result.Status.ShouldBe("Failed");
        result.Message.ShouldContain("/target_tokens");
    }

    private static ChatRenameSkill BuiltInRenameSkill() => new(new BuiltInSkillCatalog(),
        Mock.Of<IChatService>(), Mock.Of<IProjectService>(), Mock.Of<IGlobalSettingsRepository>(),
        Mock.Of<IGlobalSecretStore>(), Mock.Of<IChatCompletionClient>(), Mock.Of<IAppDataChangeSignal>(),
        NullLogger<ChatRenameSkill>.Instance);

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
