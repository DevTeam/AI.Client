namespace AI.Application.Tests.Chats;

using AI.Application.Chats;
using AI.Application.Projects;
using AI.Application.Runs;
using AI.Contracts.Chats;
using AI.Contracts.Runs;
using AI.Domain.Chats;
using AI.Domain.Projects;
using AI.Infrastructure.Storage;
using System.Text.Json.Nodes;
using Moq;
using Shouldly;
using Xunit;

public class ChatArchiveServiceTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void ShouldReadOlderDocumentsAndManifestsAsActive(int schemaVersion)
    {
        var serializer = new ChatDocumentSerializer();
        var chat = new ChatThread(new ChatId(Guid.NewGuid()), new ProjectId(Guid.NewGuid()), "Legacy chat", DateTimeOffset.UtcNow);
        var document = JsonNode.Parse(serializer.Serialize(chat, 3))!;
        var manifest = JsonNode.Parse(serializer.SerializeSummary(chat, 3))!;
        foreach (var node in new[] { document, manifest })
        {
            node["SchemaVersion"] = schemaVersion;
            node.AsObject().Remove("ArchivedAt");
            node.AsObject().Remove("ArchiveOperationId");
        }
        serializer.Deserialize(document.ToJsonString()).Chat.ArchivedAt.ShouldBeNull();
        serializer.DeserializeSummary(manifest.ToJsonString()).ArchivedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData(ChatRunStatus.Generating, false, true)]
    [InlineData(ChatRunStatus.Paused, false, true)]
    [InlineData(ChatRunStatus.Interrupted, false, true)]
    [InlineData(ChatRunStatus.Failed, false, true)]
    [InlineData(ChatRunStatus.Completed, true, true)]
    [InlineData(ChatRunStatus.Generating, true, false)]
    public async Task ShouldCheckEveryBranchAgainWhenApplying(ChatRunStatus status, bool expected, bool skipBusy)
    {
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var chat = new ChatThread(new ChatId(chatId), new ProjectId(projectId), "Old conversation", now.AddDays(-60));
        var repository = new Mock<IChatRepository>(MockBehavior.Strict);
        repository.Setup(repo => repo.GetAsync(new ProjectId(projectId), new ChatId(chatId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredChat(chat, 3));
        repository.Setup(repo => repo.SaveAsync(chat, 3, It.IsAny<CancellationToken>())).ReturnsAsync(ChatSaveResult.Saved(4));
        var runs = new Mock<IChatRunDispatcher>(MockBehavior.Strict);
        runs.Setup(dispatcher => dispatcher.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChatRunSnapshot(projectId, chatId, Guid.NewGuid(), status, "", [], false, null, 1)]);
        var clock = new Mock<IClock>();
        clock.SetupGet(value => value.UtcNow).Returns(now);
        var service = new ChatArchiveService(Mock.Of<IChatService>(), repository.Object, new ChatSynchronization(), clock.Object, () => runs.Object);

        var result = await service.ApplyAsync(projectId, new ChatArchiveRequest(true, Guid.NewGuid(), [new(chatId, 3)], SkipBusy: skipBusy), CancellationToken.None);

        (result.Changed.Count == 1).ShouldBe(expected);
        (chat.ArchivedAt is not null).ShouldBe(expected);
        repository.Verify(repo => repo.SaveAsync(chat, 3, It.IsAny<CancellationToken>()), expected ? Times.Once() : Times.Never());
        if (!expected) result.Skipped.ShouldHaveSingleItem().Reason.ShouldContain("needs attention");
    }

    [Fact]
    public async Task ShouldPermanentlyDeleteArchivedChat()
    {
        var (projectId, chatId, chat, repository, runs, service) = CreateDeleteScenario(archived: true, revision: 3);
        runs.Setup(dispatcher => dispatcher.DeleteChatAsync(projectId, chatId, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatDeleteResult(true, 3));

        var result = await service.DeleteAsync(projectId, new ChatArchiveDeleteRequest([new(chatId, 3)]), CancellationToken.None);

        result.Changed.ShouldHaveSingleItem().ChatId.ShouldBe(chatId);
        result.Skipped.ShouldBeEmpty();
        runs.Verify(dispatcher => dispatcher.DeleteChatAsync(projectId, chatId, 3, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Theory]
    [InlineData(false, 3, "no longer archived")]
    [InlineData(true, 4, "changed since")]
    public async Task ShouldNotDeleteActiveOrChangedChat(bool archived, long listedRevision, string reason)
    {
        var (projectId, chatId, _, _, runs, service) = CreateDeleteScenario(archived, revision: 3);

        var result = await service.DeleteAsync(projectId, new ChatArchiveDeleteRequest([new(chatId, listedRevision)]), CancellationToken.None);

        result.Changed.ShouldBeEmpty();
        result.Skipped.ShouldHaveSingleItem().Reason.ShouldContain(reason);
        runs.Verify(dispatcher => dispatcher.DeleteChatAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    private static (Guid ProjectId, Guid ChatId, ChatThread Chat, Mock<IChatRepository> Repository, Mock<IChatRunDispatcher> Runs, ChatArchiveService Service)
        CreateDeleteScenario(bool archived, long revision)
    {
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var chat = new ChatThread(new ChatId(chatId), new ProjectId(projectId), "Old conversation", now.AddDays(-60));
        if (archived) chat.SetArchived(true, Guid.NewGuid(), now.AddDays(-1));
        var repository = new Mock<IChatRepository>(MockBehavior.Strict);
        repository.Setup(repo => repo.GetAsync(new ProjectId(projectId), new ChatId(chatId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredChat(chat, revision));
        var runs = new Mock<IChatRunDispatcher>(MockBehavior.Strict);
        var service = new ChatArchiveService(Mock.Of<IChatService>(), repository.Object, new ChatSynchronization(), Mock.Of<IClock>(), () => runs.Object);
        return (projectId, chatId, chat, repository, runs, service);
    }
}
