namespace AI.Application.Tests.Skills;

using System.Runtime.CompilerServices;
using System.Text.Json;
using AI.Application.Chat;
using AI.Application.Chats;
using AI.Application.Notifications;
using AI.Application.Projects;
using AI.Application.Settings;
using AI.Application.Skills;
using AI.Contracts.Chat;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

public class ChatTitleSkillTests
{
    [Fact]
    public async Task ShouldReadChatThroughToolBeforeApplyingTitle()
    {
        var projectId = Guid.CreateVersion7();
        var chatId = Guid.CreateVersion7();
        var connectionId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var chat = new ChatDetails(chatId, projectId, "Explain dependency injection", now, now, 3, connectionId,
            [new ChatMessageView(Guid.CreateVersion7(), null, "User", "Explain dependency injection in C#", now),
             new ChatMessageView(Guid.CreateVersion7(), null, "Assistant", "It supplies dependencies to an object.", now)],
            AutoTitlePending: true);
        var chats = new Mock<IChatService>(MockBehavior.Strict);
        chats.Setup(item => item.GetAsync(projectId, chatId, It.IsAny<CancellationToken>())).ReturnsAsync(chat);
        chats.Setup(item => item.ApplyAutomaticTitleAsync(projectId, chatId, "Dependency injection in C#",
            It.IsAny<CancellationToken>())).ReturnsAsync(chat with { Title = "Dependency injection in C#", AutoTitlePending = false });
        var projects = new Mock<IProjectService>(MockBehavior.Strict);
        projects.Setup(item => item.GetAsync(projectId, It.IsAny<CancellationToken>())).ReturnsAsync((ProjectDetails?)null);
        var settings = new Mock<IGlobalSettingsRepository>(MockBehavior.Strict);
        settings.Setup(item => item.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalSettings([new ConnectionSettings(connectionId, "Main", "https://example.test/v1",
                "test-model", true, true, false)], [], []));
        var secrets = new Mock<IGlobalSecretStore>(MockBehavior.Strict);
        secrets.Setup(item => item.GetAsync("connection", connectionId, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        var completion = new Mock<IChatCompletionClient>(MockBehavior.Strict);
        completion.Setup(item => item.StreamAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Returns((ChatCompletionRequest request, CancellationToken token) => Respond(request, token));
        var changes = new Mock<IAppDataChangeSignal>(MockBehavior.Strict);
        changes.Setup(item => item.Notify());
        var skill = new ChatTitleSkill(new BuiltInSkillCatalog(), chats.Object, projects.Object, settings.Object,
            secrets.Object, completion.Object, changes.Object, NullLogger<ChatTitleSkill>.Instance);
        var runner = new SkillRunner(new BuiltInSkillCatalog(), skill);

        var result = await runner.RunAsync(new SkillInvocation("chat-title", projectId,
            JsonSerializer.SerializeToElement(new { chat_id = chatId, mode = "automatic" })), CancellationToken.None);

        result.Status.ShouldBe("Completed");
        result.ChatId.ShouldBe(chatId);
        chats.Verify(item => item.ApplyAutomaticTitleAsync(projectId, chatId, "Dependency injection in C#",
            It.IsAny<CancellationToken>()), Times.Once);
        changes.Verify(item => item.Notify(), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShouldRenameSelectedChatWhenRequested(bool useCurrent)
    {
        var projectId = Guid.CreateVersion7();
        var chatId = Guid.CreateVersion7();
        var connectionId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var chat = new ChatDetails(chatId, projectId, "Old title", now, now, 7, connectionId,
            [new ChatMessageView(Guid.CreateVersion7(), null, "User", "Explain dependency injection in C#", now)],
            AutoTitlePending: false);
        var chats = new Mock<IChatService>(MockBehavior.Strict);
        chats.Setup(item => item.GetAsync(projectId, chatId, It.IsAny<CancellationToken>())).ReturnsAsync(chat);
        chats.Setup(item => item.RenameAsync(projectId, chatId,
            It.Is<RenameChatRequest>(request => request.Title == "Dependency injection in C#" && request.Revision == 7),
            It.IsAny<CancellationToken>())).ReturnsAsync(chat with { Title = "Dependency injection in C#", Revision = 8 });
        var projects = new Mock<IProjectService>(MockBehavior.Strict);
        projects.Setup(item => item.GetAsync(projectId, It.IsAny<CancellationToken>())).ReturnsAsync((ProjectDetails?)null);
        var settings = new Mock<IGlobalSettingsRepository>(MockBehavior.Strict);
        settings.Setup(item => item.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalSettings([new ConnectionSettings(connectionId, "Main", "https://example.test/v1",
                "test-model", true, true, false)], [], []));
        var secrets = new Mock<IGlobalSecretStore>(MockBehavior.Strict);
        secrets.Setup(item => item.GetAsync("connection", connectionId, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        var completion = new Mock<IChatCompletionClient>(MockBehavior.Strict);
        completion.Setup(item => item.StreamAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Returns((ChatCompletionRequest request, CancellationToken token) => Respond(request, token));
        var changes = new Mock<IAppDataChangeSignal>(MockBehavior.Strict);
        changes.Setup(item => item.Notify());
        var catalog = new BuiltInSkillCatalog();
        var runner = new SkillRunner(catalog, new ChatTitleSkill(catalog, chats.Object, projects.Object, settings.Object,
            secrets.Object, completion.Object, changes.Object, NullLogger<ChatTitleSkill>.Instance));

        var currentChatId = useCurrent ? chatId : Guid.CreateVersion7();
        var result = await runner.RunAsync(new SkillInvocation("chat-title", projectId,
            JsonSerializer.SerializeToElement(new { chat_id = useCurrent ? "current" : chatId.ToString(), mode = "requested" }),
            currentChatId), CancellationToken.None);

        result.Status.ShouldBe("Completed");
        result.ChatId.ShouldBe(chatId);
        runner.ListRecent().Single().Id.ShouldBe(result.Id);
        chats.Verify(item => item.RenameAsync(projectId, chatId, It.IsAny<RenameChatRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
        changes.Verify(item => item.Notify(), Times.Once);
    }

    [Fact]
    public async Task ShouldRejectChatOutsideProjectBeforeCallingModel()
    {
        var projectId = Guid.CreateVersion7();
        var chatId = Guid.CreateVersion7();
        var chats = new Mock<IChatService>(MockBehavior.Strict);
        chats.Setup(item => item.GetAsync(projectId, chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatDetails?)null);
        var catalog = new BuiltInSkillCatalog();
        var runner = new SkillRunner(catalog, new ChatTitleSkill(catalog, chats.Object, Mock.Of<IProjectService>(),
            Mock.Of<IGlobalSettingsRepository>(), Mock.Of<IGlobalSecretStore>(),
            Mock.Of<IChatCompletionClient>(), Mock.Of<IAppDataChangeSignal>(), NullLogger<ChatTitleSkill>.Instance));

        var result = await runner.RunAsync(new SkillInvocation("chat-title", projectId,
            JsonSerializer.SerializeToElement(new { chat_id = chatId, mode = "requested" })), CancellationToken.None);

        result.Status.ShouldBe("Failed");
        result.Message.ShouldContain("not found in this project");
        chats.VerifyAll();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"chat_id\":\"not-a-guid\",\"mode\":\"automatic\"}")]
    [InlineData("{\"chat_id\":\"00000000-0000-0000-0000-000000000001\",\"mode\":\"automatic\",\"unexpected\":true}")]
    public async Task ShouldRejectInvalidSkillParametersBeforeExecution(string arguments)
    {
        var chats = new Mock<IChatService>(MockBehavior.Strict);
        var catalog = new BuiltInSkillCatalog();
        var skill = new ChatTitleSkill(catalog, chats.Object, Mock.Of<IProjectService>(),
            Mock.Of<IGlobalSettingsRepository>(), Mock.Of<IGlobalSecretStore>(),
            Mock.Of<IChatCompletionClient>(), Mock.Of<IAppDataChangeSignal>(), NullLogger<ChatTitleSkill>.Instance);
        var runner = new SkillRunner(catalog, skill);

        var result = await runner.RunAsync(
            new SkillInvocation("chat-title", Guid.CreateVersion7(), JsonSerializer.Deserialize<JsonElement>(arguments)),
            CancellationToken.None);
        result.Status.ShouldBe("Failed");
        chats.VerifyNoOtherCalls();
    }

    private static async IAsyncEnumerable<ChatCompletionChunk> Respond(ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken token)
    {
        await Task.Yield();
        token.ThrowIfCancellationRequested();
        if (request.Tools is { Count: > 0 })
        {
            request.ContextMessages!.ShouldNotContain(message => message.Content.Contains("dependency injection in C#"));
            yield return new ChatCompletionChunk(string.Empty, ToolCalls:
                [new ChatToolCall("read-1", "read_chat", "{\"start\":0,\"count\":2}")]);
        }
        else
        {
            request.ContextMessages![^1].Content.ShouldContain("Explain dependency injection in C#");
            yield return new ChatCompletionChunk("Dependency injection in C#");
        }
    }
}
