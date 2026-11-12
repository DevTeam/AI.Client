namespace AI.Application.Tests.Skills;

using System.Text.Json;
using AI.Application.Skills;
using AI.Contracts.Skills;
using Moq;
using Shouldly;
using Xunit;

public sealed class PlaybookArgumentsTests
{
    [Fact]
    public async Task APlaybookEchoesOnlyTheParametersItDeclares()
    {
        // A model starting the same playbook over invented a "language" key; echoed back, it made the
        // repeated load look like a new one and the instructions were handed out again.
        var result = await RunAsync("""{"type":"object","properties":{"path":{"type":"string"}}}""",
            """{"path":"D:\\Work","language":"ru"}""");

        var arguments = result.Output!.Value.GetProperty("arguments");
        arguments.GetProperty("path").GetString().ShouldBe("D:\\Work");
        arguments.TryGetProperty("language", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task APlaybookWithoutParametersEchoesAnEmptyObject()
    {
        var result = await RunAsync("""{"type":"object"}""", """{"language":"ru"}""");

        result.Output!.Value.GetProperty("arguments").EnumerateObject().ShouldBeEmpty();
    }

    private static async Task<SkillRunRecord> RunAsync(string schema, string parameters)
    {
        var projectId = Guid.CreateVersion7();
        var skill = new SkillDefinition("folder-review", "Folder review", "Reviews a folder", "test",
            "---\nname: folder-review\n---\nList the folder, then read what matters.", true, JsonElement.Parse(schema),
            Kind: SkillKinds.Playbook);
        var catalog = new Mock<ISkillCatalog>();
        catalog.Setup(item => item.GetByIdAsync(skill.Id, projectId, It.IsAny<CancellationToken>())).ReturnsAsync(skill);
        var runner = new SkillRunner(catalog.Object, Mock.Of<ISkillExecutor>(item => item.SkillId == "chat-rename"));
        var record = await runner.RunAsync(new SkillInvocation(skill.Id, projectId, JsonElement.Parse(parameters)),
            TestContext.Current.CancellationToken);
        record.Status.ShouldBe("Completed");
        return record;
    }
}
