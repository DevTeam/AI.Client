namespace AI.Infrastructure.Tests.Storage;

using AI.Application.Chats;
using AI.Contracts.Chats;
using AI.Domain.Chats;
using AI.Domain.Projects;
using AI.Infrastructure.Storage;
using Moq;
using Shouldly;
using Xunit;

/// <summary>
/// What every <see cref="IChatRepository"/> must do (ADR-014). A database store is a new subclass of this
/// suite; the JSON documents and the Host-lifetime store already pass it.
/// </summary>
public abstract class ChatRepositoryContractTests
{
    private readonly ProjectId _project = new(Guid.CreateVersion7());
    private readonly DateTimeOffset _now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    protected abstract IChatRepository Create();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAbsentChatReadsAsAbsence()
    {
        var repository = Create();
        var id = new ChatId(Guid.CreateVersion7());

        (await repository.GetAsync(_project, id, Token)).ShouldBeNull();
        (await repository.GetMessageAsync(_project, id, new ChatMessageId(Guid.CreateVersion7()), Token)).ShouldBeNull();
        (await repository.ListSummariesAsync(_project, Token)).ShouldBeEmpty();
        (await repository.DeleteAsync(_project, id, 0, Token)).IsDeleted.ShouldBeFalse();
    }

    [Fact]
    public async Task ASavedChatIsReadBackAtItsNewRevision()
    {
        var repository = Create();
        var chat = Chat("First");
        var question = Message(null, "Question");
        chat.AddMessage(question, _now);

        (await repository.SaveAsync(chat, 0, Token)).ShouldBe(ChatSaveResult.Saved(1));

        var stored = (await repository.GetAsync(_project, chat.Id, Token)).ShouldNotBeNull();
        stored.Revision.ShouldBe(1);
        stored.Chat.Title.ShouldBe("First");
        stored.Chat.Messages.ShouldHaveSingleItem().Content.ShouldBe("Question");
        var summary = (await repository.ListSummariesAsync(_project, Token)).ShouldHaveSingleItem();
        summary.Id.ShouldBe(chat.Id);
        summary.Revision.ShouldBe(1);
    }

    [Fact]
    public async Task ASaveFromAStaleRevisionIsRefusedAndChangesNothing()
    {
        var repository = Create();
        var chat = Chat("First");
        await repository.SaveAsync(chat, 0, Token);
        chat.Rename("Second", _now);
        await repository.SaveAsync(chat, 1, Token);

        chat.Rename("Third", _now);
        (await repository.SaveAsync(chat, 1, Token)).ShouldBe(ChatSaveResult.Conflict(2));

        (await repository.GetAsync(_project, chat.Id, Token))!.Chat.Title.ShouldBe("Second");
    }

    [Fact]
    public async Task LaterSavesKeepEarlierMessagesAndEachIsReadableOnItsOwn()
    {
        var repository = Create();
        var chat = Chat("Chat");
        var question = Message(null, "Question");
        chat.AddMessage(question, _now);
        await repository.SaveAsync(chat, 0, Token);
        var stored = (await repository.GetAsync(_project, chat.Id, Token))!.Chat;
        var answer = Message(question.Id, "Answer");
        stored.AddMessage(answer, _now);

        (await repository.SaveAsync(stored, 1, Token)).ShouldBe(ChatSaveResult.Saved(2));

        (await repository.GetAsync(_project, chat.Id, Token))!.Chat.Messages.Select(item => item.Content)
            .ShouldBe(["Question", "Answer"], ignoreOrder: true);
        (await repository.GetMessageAsync(_project, chat.Id, answer.Id, Token))!.Message!.Content.ShouldBe("Answer");
        (await repository.GetMessageAsync(_project, chat.Id, new ChatMessageId(Guid.CreateVersion7()), Token))
            .ShouldNotBeNull().Message.ShouldBeNull();
    }

    [Fact]
    public async Task ADeleteNeedsTheCurrentRevision()
    {
        var repository = Create();
        var chat = Chat("Chat");
        await repository.SaveAsync(chat, 0, Token);

        (await repository.DeleteAsync(_project, chat.Id, 0, Token)).ShouldBe(new ChatDeleteResult(false, 1));
        (await repository.DeleteAsync(_project, chat.Id, 1, Token)).ShouldBe(new ChatDeleteResult(true, 1));

        (await repository.GetAsync(_project, chat.Id, Token)).ShouldBeNull();
        (await repository.ListSummariesAsync(_project, Token)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ChatsAreListedOnlyForTheirProject()
    {
        var repository = Create();
        await repository.SaveAsync(Chat("Mine"), 0, Token);
        await repository.SaveAsync(new ChatThread(new ChatId(Guid.CreateVersion7()), new ProjectId(Guid.CreateVersion7()),
            "Other", _now), 0, Token);

        (await repository.ListSummariesAsync(_project, Token)).ShouldHaveSingleItem().Title.ShouldBe("Mine");
    }

    private ChatThread Chat(string title) => new(new ChatId(Guid.CreateVersion7()), _project, title, _now);

    private ChatMessage Message(ChatMessageId? parent, string content) =>
        new(new ChatMessageId(Guid.CreateVersion7()), parent, ChatMessageRole.User, content, _now);
}

public sealed class JsonChatRepositoryContractTests : ChatRepositoryContractTests
{
    protected override IChatRepository Create() =>
        new JsonChatRepository(new MemoryFileSystem(),
            new ChatStoragePaths(Mock.Of<IUserStorageLocation>(location => location.RootDirectory == "data")),
            new ChatDocumentSerializer());
}

public sealed class HostLifetimeChatRepositoryContractTests : ChatRepositoryContractTests
{
    protected override IChatRepository Create() => new HostLifetimeChatRepository(new ChatDocumentSerializer());
}
