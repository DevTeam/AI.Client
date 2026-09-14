namespace AI.Client.Infrastructure.Tests.Storage;

using AI.Client.Infrastructure.Storage;
using Domain.Chats;
using AI.Client.Domain.Projects;
using Shouldly;
using Xunit;

public sealed class ChatGraphStorageTests
{
    [Fact]
    public async Task ShouldCommitChatDocumentAtomically()
    {
        var fs = new MemoryFileSystem();
        var paths = new ChatStoragePaths("data");
        using var repository = new JsonChatRepository(fs, paths);
        var now = DateTimeOffset.UtcNow;
        var chat = new ChatThread(new ChatId(Guid.NewGuid()), new ProjectId(Guid.NewGuid()), "Chat", now);
        await repository.SaveAsync(chat, 0, CancellationToken.None);
        var message = new ChatMessage(new ChatMessageId(Guid.NewGuid()), null, ChatMessageRole.User, "Question", now);
        chat.AddMessage(message, now);
        fs.FailWriteSuffix = ".json.tmp";
        await Should.ThrowAsync<IOException>(() => repository.SaveAsync(chat, 1, CancellationToken.None));
        (await repository.GetAsync(chat.ProjectId, chat.Id, CancellationToken.None))!.Chat.Messages.ShouldBeEmpty();
        fs.FailWriteSuffix = null;
        await repository.SaveAsync(chat, 1, CancellationToken.None);
        var restored = await repository.GetAsync(chat.ProjectId, chat.Id, CancellationToken.None);
        restored!.Chat.Messages.ShouldHaveSingleItem().Content.ShouldBe("Question");
        fs.Files[paths.GetChatPath(chat.Id, chat.ProjectId)].ShouldContain("Question");
    }

    [Fact]
    public async Task ShouldRejectOneOfTwoConcurrentWritesWithTheSameRevision()
    {
        var fs = new MemoryFileSystem();
        using var repository = new JsonChatRepository(fs, new ChatStoragePaths("data"));
        var chat = new ChatThread(new ChatId(Guid.NewGuid()), new ProjectId(Guid.NewGuid()), "Chat", DateTimeOffset.UtcNow);
        await repository.SaveAsync(chat, 0, CancellationToken.None);
        var writes = await Task.WhenAll(repository.SaveAsync(chat, 1, CancellationToken.None), repository.SaveAsync(chat, 1, CancellationToken.None));
        writes.Count(result => result.IsSaved).ShouldBe(1);
        writes.Select(result => result.Revision).ShouldAllBe(revision => revision == 2);
    }

    [Fact]
    public async Task ShouldPreserveBranchHeadsAndNamesAcrossRoundTrip()
    {
        var fs = new MemoryFileSystem();
        using var repository = new JsonChatRepository(fs, new ChatStoragePaths("data"));
        var now = DateTimeOffset.UtcNow;
        var chat = new ChatThread(new ChatId(Guid.NewGuid()), new ProjectId(Guid.NewGuid()), "Chat", now);
        var root = new ChatMessage(new ChatMessageId(Guid.NewGuid()), null, ChatMessageRole.User, "Root", now);
        var original = new ChatMessage(new ChatMessageId(Guid.NewGuid()), root.Id, ChatMessageRole.Assistant, "Original", now);
        var fork = new ChatMessage(new ChatMessageId(Guid.NewGuid()), root.Id, ChatMessageRole.User, "Fork", now);
        chat.AddMessage(root, now);
        chat.AddMessage(original, now);
        chat.AddMessage(fork, now, fork.Id.Value);
        chat.RenameBranch(fork.Id, "Alternative", now);
        await repository.SaveAsync(chat, 0, CancellationToken.None);
        var restored = (await repository.GetAsync(chat.ProjectId, chat.Id, CancellationToken.None))!.Chat;
        restored.Branches.Single(branch => branch.Id == chat.Id.Value).HeadMessageId.ShouldBe(original.Id);
        restored.Branches.Single(branch => branch.Id == fork.Id.Value).Title.ShouldBe("Alternative");
        restored.GetBranch(fork.Id).Select(message => message.Content).ShouldBe(["Root", "Fork"]);
    }
}
