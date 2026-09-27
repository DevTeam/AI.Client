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
        var paths = new ChatStoragePaths("data");
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
        var paths = new ChatStoragePaths("data");
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
    public async Task ShouldMigrateBranchCountForAnOlderSummary()
    {
        var projectId = new ProjectId(Guid.NewGuid());
        var chatId = new ChatId(Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;
        var fs = new MemoryFileSystem();
        var paths = new ChatStoragePaths("data");
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
        summary.HasStoredBranchCount.ShouldBeTrue();
        JsonNode.Parse(fs.Files[summaryPath])!["BranchCount"]!.GetValue<int>().ShouldBe(1);

        while (fs.ReadPaths.TryDequeue(out _)) { }
        await repository.ListSummariesAsync(projectId, CancellationToken.None);
        fs.ReadPaths.ShouldAllBe(path => path == summaryPath);
    }

    [Fact]
    public async Task ShouldOnlyReadTheChatDocumentWhenAppending()
    {
        var projectId = new ProjectId(Guid.NewGuid());
        var chatId = new ChatId(Guid.NewGuid());
        var firstId = new ChatMessageId(Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;
        var fs = new MemoryFileSystem();
        var paths = new ChatStoragePaths("data");
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
