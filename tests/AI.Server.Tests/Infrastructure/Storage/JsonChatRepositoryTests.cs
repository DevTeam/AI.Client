using Moq;

namespace AI.Infrastructure.Tests.Storage;

using AI.Domain.Projects;
using AI.Domain.Chats;
using AI.Infrastructure.Storage;
using Shouldly;
using System.Text.Json.Nodes;
using Xunit;

public sealed class JsonChatRepositoryTests
{
    [Fact]
    public async Task ShouldIgnoreRunDocumentsWhenListingChats()
    {
        var projectId = new ProjectId(Guid.NewGuid());
        var fs = new MemoryFileSystem();
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(i => i.RootDirectory).Returns("data");
        var paths = new ChatStoragePaths(location.Object);
        fs.Files[Path.Combine(paths.GetChatsDirectory(projectId), "chat.run.json")] = "{}";
        using var repository = new JsonChatRepository(fs, paths, new ChatDocumentSerializer());
        (await repository.ListSummariesAsync(projectId, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ShouldListChatSummaryWithoutReadingMessageNodes()
    {
        var projectId = new ProjectId(Guid.NewGuid());
        var chatId = new ChatId(Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;
        var fs = new MemoryFileSystem();
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(i => i.RootDirectory).Returns("data");
        var paths = new ChatStoragePaths(location.Object);
        var chat = new ChatThread(chatId, projectId, "Large chat", now);
        chat.AddMessage(new ChatMessage(new ChatMessageId(Guid.NewGuid()), null, ChatMessageRole.User, "Question", now), now);
        using var repository = new JsonChatRepository(fs, paths, new ChatDocumentSerializer());
        await repository.SaveAsync(chat, 0, CancellationToken.None);
        while (fs.ReadPaths.TryDequeue(out _)) { }

        var summary = (await repository.ListSummariesAsync(projectId, CancellationToken.None)).ShouldHaveSingleItem();

        summary.Id.ShouldBe(chatId);
        summary.Title.ShouldBe("Large chat");
        fs.ReadPaths.ShouldAllBe(path => path == paths.GetChatSummaryPath(chatId, projectId));
    }

    [Fact]
    public async Task ShouldReadOnlyTheRequestedReviewSourceMessage()
    {
        var projectId = new ProjectId(Guid.NewGuid());
        var chatId = new ChatId(Guid.NewGuid());
        var firstId = new ChatMessageId(Guid.NewGuid());
        var sourceId = new ChatMessageId(Guid.NewGuid());
        var reviewId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var files = new MemoryFileSystem();
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(item => item.RootDirectory).Returns("data");
        var paths = new ChatStoragePaths(location.Object);
        var chat = new ChatThread(chatId, projectId, "Chat", now);
        chat.AddMessage(new ChatMessage(firstId, null, ChatMessageRole.User, "Question", now, resources:
            [new AI.Domain.Resources.ChatResource(reviewId, AI.Domain.Resources.ChatResourceKind.Review, string.Empty)]), now);
        chat.AddMessage(new ChatMessage(sourceId, firstId, ChatMessageRole.Assistant, "Answer", now.AddSeconds(1)),
            now.AddSeconds(1));
        using var repository = new JsonChatRepository(files, paths, new ChatDocumentSerializer());
        await repository.SaveAsync(chat, 0, CancellationToken.None);

        var found = await repository.GetMessageAsync(projectId, chatId, sourceId, CancellationToken.None);

        found.ShouldNotBeNull().Message.ShouldNotBeNull().Content.ShouldBe("Answer");
        (await repository.GetMessageAsync(projectId, chatId, new ChatMessageId(Guid.NewGuid()),
            CancellationToken.None)).ShouldNotBeNull().Message.ShouldBeNull();
        (await repository.MayContainReviewReferenceAsync(projectId, chatId, Guid.NewGuid(),
            CancellationToken.None)).ShouldBe(false);
        (await repository.MayContainReviewReferenceAsync(projectId, chatId, reviewId,
            CancellationToken.None)).ShouldBe(true);
    }

    [Fact]
    public async Task ShouldReuseLoadedChatForReviewLookupsAndInvalidateAfterSave()
    {
        var projectId = new ProjectId(Guid.NewGuid());
        var chatId = new ChatId(Guid.NewGuid());
        var sourceId = new ChatMessageId(Guid.NewGuid());
        var reviewId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var files = new MemoryFileSystem();
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(item => item.RootDirectory).Returns("data");
        var paths = new ChatStoragePaths(location.Object);
        var chat = new ChatThread(chatId, projectId, "Chat", now);
        chat.AddMessage(new ChatMessage(sourceId, null, ChatMessageRole.User, "Question", now,
            resources: [new AI.Domain.Resources.ChatResource(reviewId, AI.Domain.Resources.ChatResourceKind.Review, string.Empty)]), now);
        using var repository = new JsonChatRepository(files, paths, new ChatDocumentSerializer());
        await repository.SaveAsync(chat, 0, CancellationToken.None);
        await repository.GetAsync(projectId, chatId, CancellationToken.None);
        var scheduledChat = new ChatThread(new ChatId(Guid.NewGuid()), projectId, "Scheduled", now);
        scheduledChat.AddMessage(new ChatMessage(new ChatMessageId(Guid.NewGuid()), null,
            ChatMessageRole.User, "Reminder", now), now);
        await repository.SaveAsync(scheduledChat, 0, CancellationToken.None);
        await repository.GetAsync(projectId, scheduledChat.Id, CancellationToken.None);
        while (files.ReadPaths.TryDequeue(out _)) { }

        (await repository.GetMessageAsync(projectId, chatId, sourceId, CancellationToken.None))!
            .Message!.Content.ShouldBe("Question");
        (await repository.MayContainReviewReferenceAsync(projectId, chatId, reviewId,
            CancellationToken.None)).ShouldBe(true);
        (await repository.MayContainReviewReferenceAsync(projectId, chatId, Guid.NewGuid(),
            CancellationToken.None)).ShouldBe(false);
        files.ReadPaths.ShouldBeEmpty();

        chat.RemoveReviewReferences(reviewId, now.AddSeconds(1)).ShouldBeTrue();
        (await repository.SaveAsync(chat, 1, CancellationToken.None)).IsSaved.ShouldBeTrue();
        while (files.ReadPaths.TryDequeue(out _)) { }

        // The repository keeps what it wrote, so its own save is answered without a read.
        (await repository.MayContainReviewReferenceAsync(projectId, chatId, reviewId,
            CancellationToken.None)).ShouldBe(false);
        files.ReadPaths.ShouldNotContain(paths.GetChatPath(chatId, projectId));

        await repository.GetAsync(projectId, chatId, CancellationToken.None);
        while (files.ReadPaths.TryDequeue(out _)) { }
        var nextId = new ChatMessageId(Guid.NewGuid());
        chat.AddMessage(new ChatMessage(nextId, sourceId, ChatMessageRole.Assistant, "Answer",
            now.AddSeconds(2)), now.AddSeconds(2));
        using var otherRepository = new JsonChatRepository(files, paths, new ChatDocumentSerializer());
        (await otherRepository.SaveAsync(chat, 2, CancellationToken.None)).IsSaved.ShouldBeTrue();
        while (files.ReadPaths.TryDequeue(out _)) { }

        (await repository.GetMessageAsync(projectId, chatId, nextId, CancellationToken.None))!
            .Message!.Content.ShouldBe("Answer");
        files.ReadPaths.ShouldContain(paths.GetChatPath(chatId, projectId));
    }

    [Fact]
    public async Task ShouldMigrateBranchCountForAnOlderSummary()
    {
        var projectId = new ProjectId(Guid.NewGuid());
        var chatId = new ChatId(Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;
        var fs = new MemoryFileSystem();
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(i => i.RootDirectory).Returns("data");
        var paths = new ChatStoragePaths(location.Object);
        var chat = new ChatThread(chatId, projectId, "Branched chat", now);
        var root = new ChatMessage(new ChatMessageId(Guid.NewGuid()), null, ChatMessageRole.User, "Question", now);
        var branchHead = new ChatMessage(new ChatMessageId(Guid.NewGuid()), root.Id, ChatMessageRole.Assistant, "Alternative", now);
        chat.AddMessage(root, now);
        chat.AddMessage(branchHead, now, branchHead.Id.Value, chat.Id.Value);
        using var repository = new JsonChatRepository(fs, paths, new ChatDocumentSerializer());
        await repository.SaveAsync(chat, 0, CancellationToken.None);

        var summaryPath = paths.GetChatSummaryPath(chatId, projectId);
        var oldSummary = JsonNode.Parse(fs.Files[summaryPath])!.AsObject();
        oldSummary.Remove("BranchCount");
        fs.Files[summaryPath] = oldSummary.ToJsonString();
        while (fs.ReadPaths.TryDequeue(out _)) { }

        var summary = (await repository.ListSummariesAsync(projectId, CancellationToken.None)).ShouldHaveSingleItem();

        summary.BranchCount.ShouldBe(1);
        summary.HasCurrentManifest.ShouldBeTrue();
        JsonNode.Parse(fs.Files[summaryPath])!["BranchCount"]!.GetValue<int>().ShouldBe(1);

        while (fs.ReadPaths.TryDequeue(out _)) { }
        await repository.ListSummariesAsync(projectId, CancellationToken.None);
        fs.ReadPaths.ShouldAllBe(path => path == summaryPath);
    }

    [Fact]
    public async Task ShouldUpgradeLegacyDemoSummaryFromItsFullDocument()
    {
        var projectId = new ProjectId(Guid.NewGuid());
        var chatId = new ChatId(Guid.NewGuid());
        var fs = new MemoryFileSystem();
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(item => item.RootDirectory).Returns("data");
        var paths = new ChatStoragePaths(location.Object);
        using var repository = new JsonChatRepository(fs, paths, new ChatDocumentSerializer());
        await repository.SaveAsync(new ChatThread(chatId, projectId, "Guide demo", DateTimeOffset.UnixEpoch),
            0, CancellationToken.None);
        var chatPath = paths.GetChatPath(chatId, projectId);
        var summaryPath = paths.GetChatSummaryPath(chatId, projectId);
        var oldChat = JsonNode.Parse(fs.Files[chatPath])!.AsObject();
        oldChat["SchemaVersion"] = 8;
        oldChat.Remove("Kind");
        oldChat.Remove("KindState");
        oldChat.Remove("KindStateVersion");
        oldChat["GuideMode"] = "demo";
        fs.Files[chatPath] = oldChat.ToJsonString();
        var oldSummary = JsonNode.Parse(fs.Files[summaryPath])!.AsObject();
        oldSummary["SchemaVersion"] = 8;
        oldSummary.Remove("Kind");
        fs.Files[summaryPath] = oldSummary.ToJsonString();

        var summary = (await repository.ListSummariesAsync(projectId, CancellationToken.None)).ShouldHaveSingleItem();

        summary.Kind.ShouldBe(ChatKind.Demo);
        JsonNode.Parse(fs.Files[summaryPath])!["Kind"]!.GetValue<string>().ShouldBe(ChatKind.Demo.Value);
    }

    [Fact]
    public async Task ShouldServeAToolCallsReadsAndRevisionCheckFromTheCachedChat()
    {
        // A tool call in the agent loop reads the chat at its start, at its end, before appending
        // its result and inside the append, then saves. Each of those used to parse the whole
        // transcript, and the save read it once more for the revision.
        var projectId = new ProjectId(Guid.NewGuid());
        var chatId = new ChatId(Guid.NewGuid());
        var firstId = new ChatMessageId(Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;
        var files = new MemoryFileSystem();
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(item => item.RootDirectory).Returns("data");
        var paths = new ChatStoragePaths(location.Object);
        var chatPath = paths.GetChatPath(chatId, projectId);
        var chat = new ChatThread(chatId, projectId, "Chat", now);
        chat.AddMessage(new ChatMessage(firstId, null, ChatMessageRole.User, "Question", now), now);
        using var repository = new JsonChatRepository(files, paths, new ChatDocumentSerializer());
        await repository.SaveAsync(chat, 0, CancellationToken.None);
        while (files.ReadPaths.TryDequeue(out _)) { }

        var head = firstId;
        for (var call = 1; call <= 3; call++)
        {
            await repository.GetAsync(projectId, chatId, CancellationToken.None);
            await repository.GetAsync(projectId, chatId, CancellationToken.None);
            await repository.GetAsync(projectId, chatId, CancellationToken.None);
            var stored = (await repository.GetAsync(projectId, chatId, CancellationToken.None))!;
            var next = new ChatMessageId(Guid.NewGuid());
            stored.Chat.AddMessage(new ChatMessage(next, head, ChatMessageRole.Tool, $"Result {call}",
                now.AddSeconds(call)), now.AddSeconds(call));
            (await repository.SaveAsync(stored.Chat, stored.Revision, CancellationToken.None)).IsSaved.ShouldBeTrue();
            head = next;
        }

        files.ReadPaths.ShouldNotContain(chatPath);
        var reread = new ChatDocumentSerializer().Deserialize(files.Files[chatPath]);
        reread.Revision.ShouldBe(4);
        reread.Chat.Messages.Count.ShouldBe(4);
        (await repository.GetAsync(projectId, chatId, CancellationToken.None))!.Chat.Messages
            .Select(message => message.Content).ShouldBe(["Question", "Result 1", "Result 2", "Result 3"]);
    }

    [Fact]
    public async Task ShouldHandOutChatsThatChangeApartFromTheCache()
    {
        var projectId = new ProjectId(Guid.NewGuid());
        var chatId = new ChatId(Guid.NewGuid());
        var firstId = new ChatMessageId(Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;
        var files = new MemoryFileSystem();
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(item => item.RootDirectory).Returns("data");
        var paths = new ChatStoragePaths(location.Object);
        var chat = new ChatThread(chatId, projectId, "Chat", now);
        chat.AddMessage(new ChatMessage(firstId, null, ChatMessageRole.User, "Question", now), now);
        using var repository = new JsonChatRepository(files, paths, new ChatDocumentSerializer());
        await repository.SaveAsync(chat, 0, CancellationToken.None);

        // The caller's own instance, changed after the save, and a read whose save then conflicts.
        chat.Rename("Changed by the caller", now.AddSeconds(1));
        var read = (await repository.GetAsync(projectId, chatId, CancellationToken.None))!;
        read.Chat.Rename("Changed by a reader", now.AddSeconds(1));
        read.Chat.AddMessage(new ChatMessage(new ChatMessageId(Guid.NewGuid()), firstId,
            ChatMessageRole.Assistant, "Never saved", now.AddSeconds(1)), now.AddSeconds(1));
        (await repository.SaveAsync(read.Chat, 0, CancellationToken.None)).IsSaved.ShouldBeFalse();

        var current = (await repository.GetAsync(projectId, chatId, CancellationToken.None))!;
        current.Chat.Title.ShouldBe("Chat");
        current.Chat.Messages.ShouldHaveSingleItem();
        current.Revision.ShouldBe(1);
    }

    [Fact]
    public async Task ShouldReadAgainAndCheckTheStoredRevisionAfterAnotherWriter()
    {
        var projectId = new ProjectId(Guid.NewGuid());
        var chatId = new ChatId(Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;
        var files = new MemoryFileSystem();
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(item => item.RootDirectory).Returns("data");
        var paths = new ChatStoragePaths(location.Object);
        var chatPath = paths.GetChatPath(chatId, projectId);
        using var repository = new JsonChatRepository(files, paths, new ChatDocumentSerializer());
        using var otherRepository = new JsonChatRepository(files, paths, new ChatDocumentSerializer());
        await repository.SaveAsync(new ChatThread(chatId, projectId, "Chat", now), 0, CancellationToken.None);
        var stale = (await repository.GetAsync(projectId, chatId, CancellationToken.None))!;
        var other = (await otherRepository.GetAsync(projectId, chatId, CancellationToken.None))!;
        other.Chat.Rename("Renamed elsewhere", now.AddSeconds(1));
        (await otherRepository.SaveAsync(other.Chat, other.Revision, CancellationToken.None)).IsSaved.ShouldBeTrue();
        while (files.ReadPaths.TryDequeue(out _)) { }

        stale.Chat.Rename("Renamed here", now.AddSeconds(2));
        var result = await repository.SaveAsync(stale.Chat, stale.Revision, CancellationToken.None);

        result.IsSaved.ShouldBeFalse();
        result.Revision.ShouldBe(2);
        files.ReadPaths.ShouldContain(chatPath);
        (await repository.GetAsync(projectId, chatId, CancellationToken.None))!.Chat.Title.ShouldBe("Renamed elsewhere");
    }

    [Fact]
    public void ShouldCloneAChatAsItsDocumentWouldReadBack()
    {
        var projectId = new ProjectId(Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;
        var chat = new ChatThread(new ChatId(Guid.NewGuid()), projectId, "Chat", now);
        var question = new ChatMessageId(Guid.NewGuid());
        chat.AddMessage(new ChatMessage(question, null, ChatMessageRole.User, "Question", now), now);
        chat.AddMessage(new ChatMessage(new ChatMessageId(Guid.NewGuid()), question, ChatMessageRole.Assistant,
            "First answer", now.AddSeconds(1)), now.AddSeconds(1));
        chat.AddMessage(new ChatMessage(new ChatMessageId(Guid.NewGuid()), question, ChatMessageRole.Assistant,
            "Second answer", now.AddSeconds(2)), now.AddSeconds(2), Guid.NewGuid(), branchTitle: "Retry");
        chat.SetToolPolicy(new ToolPolicy(new ToolIdentity(new McpServerId(Guid.NewGuid()), "read", "hash"),
            ToolPolicyDecision.Allow), now.AddSeconds(3));
        chat.Pin("a", now.AddSeconds(3));
        chat.SetApprovalMode(ChatApprovalMode.Ask, now.AddSeconds(3));
        chat.SetReasoningEffort("high", now.AddSeconds(3));
        var serializer = new ChatDocumentSerializer();

        var read = serializer.Deserialize(serializer.Serialize(chat, 7)).Chat;

        serializer.Serialize(chat.Clone(), 7).ShouldBe(serializer.Serialize(read, 7));
        serializer.Serialize(read.Clone(), 7).ShouldBe(serializer.Serialize(read, 7));
    }

    [Fact]
    public async Task ShouldOnlyReadTheChatDocumentWhenAppending()
    {
        var projectId = new ProjectId(Guid.NewGuid());
        var chatId = new ChatId(Guid.NewGuid());
        var firstId = new ChatMessageId(Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;
        var fs = new MemoryFileSystem();
        var location = new Mock<IProjectStorageLocation>();
        location.SetupGet(i => i.RootDirectory).Returns("data");
        var paths = new ChatStoragePaths(location.Object);
        var chat = new ChatThread(chatId, projectId, "Chat", now);
        chat.AddMessage(new ChatMessage(firstId, null, ChatMessageRole.User, "Question", now), now);
        using var repository = new JsonChatRepository(fs, paths, new ChatDocumentSerializer());
        await repository.SaveAsync(chat, 0, CancellationToken.None);
        while (fs.ReadPaths.TryDequeue(out _)) { }
        chat.AddMessage(new ChatMessage(new ChatMessageId(Guid.NewGuid()), firstId, ChatMessageRole.Assistant, "Answer", now), now);

        await repository.SaveAsync(chat, 1, CancellationToken.None);

        fs.ReadPaths.ShouldAllBe(path => path == paths.GetChatPath(chatId, projectId));
    }
}
