namespace AI.Application.Tests.Skills;

using System.Runtime.CompilerServices;
using System.Text.Json;
using AI.Application.Chat;
using AI.Application.Projects;
using AI.Application.Settings;
using AI.Application.Skills;
using AI.Contracts.Projects;
using AI.Contracts.Settings;
using Moq;
using Shouldly;
using Xunit;

public sealed class GenericSkillExecutorTests
{
    [Theory]
    [InlineData("{\"name\":\"C# project assistant\"}", "Completed")]
    [InlineData("{\"name\":\"x\"}", "Failed")]
    public async Task ShouldUseCallerSuppliedProjectDetailsAndValidateStructuredResult(string answer, string expectedStatus)
    {
        var projectId = Guid.CreateVersion7();
        var connectionId = Guid.CreateVersion7();
        var projects = new Mock<IProjectService>();
        projects.Setup(service => service.GetAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectDetails(projectId, "Untitled app", "A C# assistant for project work",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 4, [], [], [], connectionId));
        var settings = new Mock<IGlobalSettingsRepository>();
        settings.Setup(service => service.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalSettings([new ConnectionSettings(connectionId, "Test", "https://example.test/v1",
                "model", true, true, false)], [], []));
        var secrets = new Mock<IGlobalSecretStore>();
        var completion = new Mock<IChatCompletionClient>();
        completion.Setup(client => client.StreamAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Returns((ChatCompletionRequest request, CancellationToken token) => Respond(request, answer, token));
        var executor = new GenericSkillExecutor(projects.Object, settings.Object, secrets.Object, completion.Object);
        var skill = SkillMarkdown.Parse("""
            ---
            id: project-name-suggest
            name: Project name suggest
            description: Suggest a project name from its details; changes nothing.
            parameters: {"type":"object","properties":{"name":{"type":"string"},"description":{"type":"string"}},"required":["name","description"],"additionalProperties":false}
            result: {"type":"object","properties":{"name":{"type":"string","minLength":3,"maxLength":80}},"required":["name"],"additionalProperties":false}
            ---

            Return one JSON object with a short `name` for the project.
            """, "User");

        var result = await executor.RunAsync(skill, new SkillInvocation(skill.Id, projectId,
            JsonSerializer.SerializeToElement(new { name = "Untitled app", description = "A C# assistant for project work" })),
            CancellationToken.None);

        result.Status.ShouldBe(expectedStatus);
        if (expectedStatus == "Completed")
            result.Output!.Value.GetProperty("name").GetString().ShouldBe("C# project assistant");
    }

    private static async IAsyncEnumerable<ChatCompletionChunk> Respond(ChatCompletionRequest request, string answer,
        [EnumeratorCancellation] CancellationToken token)
    {
        await Task.Yield();
        token.ThrowIfCancellationRequested();
        request.Tools.ShouldBeEmpty();
        request.ContextMessages![^1].Content.ShouldContain("A C# assistant");
        yield return new ChatCompletionChunk(answer);
    }
}
