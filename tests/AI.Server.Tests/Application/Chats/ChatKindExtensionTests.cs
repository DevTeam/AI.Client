namespace AI.Application.Tests.Chats;

using System.Text.Json;
using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Runs;
using AI.Contracts.Chats;
using AI.Domain.Chats;
using AI.Domain.Runs;
using AI.Infrastructure.Storage;
using AI.Infrastructure.Tests.Storage;
using Moq;
using Shouldly;
using Xunit;

public sealed class ChatKindExtensionTests
{
    [Fact]
    public async Task NewPolicyRoutesChatsAndRunsToHostLifetimeStorageWithoutChangingCommonServices()
    {
        var kind = ExtensionChatKindPolicy.KindValue;
        var kinds = new ChatKindPolicyRegistry([new ExtensionChatKindPolicy()]);
        var persistentChats = new Mock<IPersistentChatRepository>(MockBehavior.Strict);
        persistentChats.Setup(repository => repository.ListSummariesAsync(It.IsAny<AI.Domain.Projects.ProjectId>(),
            It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var hostChats = new HostLifetimeChatRepository(new ChatDocumentSerializer());
        var chats = new ChatRepositoryRouter(persistentChats.Object, hostChats, () => kinds);
        var ids = new Mock<IIdGenerator>();
        var chatId = Guid.CreateVersion7();
        var projectId = Guid.CreateVersion7();
        ids.Setup(generator => generator.Create()).Returns(chatId);
        var clock = new Mock<IClock>();
        clock.SetupGet(source => source.UtcNow).Returns(DateTimeOffset.UnixEpoch);
        var service = new ChatService(chats, ids.Object, clock.Object, new ChatSynchronization(), new PinOrderKeys(), () => kinds);

        var created = await service.CreateAsync(projectId,
            new CreateChatRequest("Fixture", Kind: kind.Value,
                KindState: JsonSerializer.SerializeToElement(new { marker = 42 }), KindStateVersion: 3),
            CancellationToken.None);

        created.Kind.ShouldBe(kind.Value);
        created.KindStateVersion.ShouldBe(3);
        (await service.GetAsync(projectId, chatId, CancellationToken.None))!.KindState!.Value
            .GetProperty("marker").GetInt32().ShouldBe(42);
        (await service.ListAsync(projectId, CancellationToken.None)).Single().Kind.ShouldBe(kind.Value);
        persistentChats.Verify(repository => repository.SaveAsync(It.IsAny<ChatThread>(),
            It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);

        var persistentRuns = new Mock<IPersistentChatRunRepository>(MockBehavior.Strict);
        var hostRuns = new HostLifetimeChatRunRepository();
        var runs = new ChatRunRepositoryRouter(persistentRuns.Object, hostRuns, chats, () => kinds);
        await runs.SaveAsync(new ChatRunState(projectId, chatId, chatId), CancellationToken.None);
        (await runs.GetAsync(projectId, chatId, chatId, CancellationToken.None)).ShouldNotBeNull();
        persistentRuns.Verify(repository => repository.SaveAsync(It.IsAny<ChatRunState>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnattendedQueueRemainsUnattendedAfterRunReload()
    {
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(item => item.RootDirectory).Returns("data");
        using var repository = new JsonChatRunRepository(new MemoryFileSystem(),
            new ChatRunStoragePaths(location.Object));
        var projectId = Guid.CreateVersion7();
        var chatId = Guid.CreateVersion7();
        var state = new ChatRunState(projectId, chatId, chatId);
        state.Enqueue(Guid.CreateVersion7(), new QueuedRunMessage(Guid.CreateVersion7(), "Run", DateTimeOffset.UnixEpoch,
            Interactive: false));

        await repository.SaveAsync(state, CancellationToken.None);

        (await repository.GetAsync(projectId, chatId, chatId, CancellationToken.None))!
            .Queue.Single().Interactive.ShouldBeFalse();
    }

    [Fact]
    public void RegistryRejectsDuplicateKindsAndDoesNotTreatUnknownKindsAsConversations()
    {
        Should.Throw<InvalidOperationException>(() => new ChatKindPolicyRegistry(
            [new ExtensionChatKindPolicy(), new ExtensionChatKindPolicy()]));
        var registry = new ChatKindPolicyRegistry([new ExtensionChatKindPolicy()]);
        registry.TryResolve(new ChatKind("other")).ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => registry.Resolve(new ChatKind("other")));
    }

}
