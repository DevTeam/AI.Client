namespace AI.Application.Tests.Runs;

using AI.Application.Chat;
using AI.Application.Tools;
using AI.Application.Workspace;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Runs;
using AI.Contracts.Settings;
using AI.Domain.Users;
using AI.Infrastructure.Tests.Storage;
using AI.Server.Hosting;
using Moq;
using Shouldly;
using Xunit;

/// <summary>The run registry is shared by every user; each sees and reaches only its own runs.</summary>
public sealed class RunOwnershipTests
{
    private static readonly UserId Other = new(Guid.Parse("019f0000-0000-7000-8000-000000000001"));

    [Fact]
    public async Task AnotherUserNeitherSeesNorReachesARun()
    {
        var token = TestContext.Current.CancellationToken;
        using var composition = new TwoUserComposition(new ServerOptions("data", null, true), new MemoryFileSystem(),
            Mock.Of<IChatCompletionClient>(), Mock.Of<IToolSessionFactory>(), Mock.Of<IWorkspaceChangeTracker>());
        Guid projectId, chatId;
        using (composition.Users.Enter(UserId.Local))
        {
            await composition.Settings.SaveAsync(new GlobalSettings(
                [new ConnectionSettings(Guid.NewGuid(), "Test", "https://example.test/v1", "model", true, true, false)], [], [],
                new ChatAutomationSettings(AutoTitle: false, SuggestReplies: false)), token);
            projectId = (await composition.Projects.CreateAsync(new CreateProjectRequest("Project", ""), token)).Id;
            chatId = (await composition.Chats.CreateAsync(projectId, new CreateChatRequest("Chat"), token)).Id;
            await composition.Dispatcher.SubmitAsync(projectId, chatId,
                new SubmitChatMessageRequest(Guid.NewGuid(), Guid.NewGuid(), "Hello", ChatSubmitMode.Queue, chatId), token);
            (await composition.Dispatcher.GetSnapshotAsync(token)).ShouldHaveSingleItem().ChatId.ShouldBe(chatId);
        }

        using (composition.Users.Enter(Other))
        {
            (await composition.Dispatcher.GetSnapshotAsync(token)).ShouldBeEmpty();
            (await composition.Dispatcher.StopAsync(projectId, chatId, chatId, token)).ShouldBeNull();
            (await composition.Dispatcher.AnswerPromptAsync(projectId, chatId, chatId,
                new UserPromptResponse(Guid.NewGuid(), UserPromptOutcome.Answered, []), token)).ShouldBeFalse();
            await using var updates = composition.Dispatcher.SubscribeAsync(token).GetAsyncEnumerator(token);
            (await updates.MoveNextAsync()).ShouldBeTrue();
            updates.Current.ShouldBeEmpty();
        }

        using (composition.Users.Enter(UserId.Local))
        {
            (await composition.Dispatcher.GetSnapshotAsync(token)).ShouldHaveSingleItem();
        }

        await composition.Dispatcher.ShutdownAsync(token);
    }
}
