using AI.Client.Application.Chats;
using AI.Client.Application.Projects;
using AI.Client.Contracts.Chats;
using AI.Client.Domain.Chats;
using AI.Client.Domain.Projects;
using Moq;
using Shouldly;
using Xunit;

namespace AI.Client.Application.Tests.Chats;

public class ChatServiceTests
{
    private readonly Mock<IChatRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IProjectIdGenerator> _idGenerator = new(MockBehavior.Strict);
    private readonly Mock<IClock> _clock = new(MockBehavior.Strict);
    private readonly ProjectId _projectId = new(Guid.Parse("019f0000-0000-7000-8000-000000000001"));
    private readonly ChatId _chatId = new(Guid.Parse("019f0000-0000-7000-8000-000000000002"));
    private readonly DateTimeOffset _now = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ShouldRenameChatUsingExpectedRevision()
    {
        var chat = new ChatThread(_chatId, _projectId, "Chat", _now);
        _repository.Setup(i => i.GetAsync(_projectId, _chatId, CancellationToken.None))
            .ReturnsAsync(new StoredChat(chat, 2));
        _clock.SetupGet(i => i.UtcNow).Returns(_now.AddMinutes(1));
        _repository.Setup(i => i.SaveAsync(chat, 2, CancellationToken.None))
            .ReturnsAsync(ChatSaveResult.Saved(3));

        var result = await CreateInstance().RenameAsync(
            _projectId.Value,
            _chatId.Value,
            new RenameChatRequest("Renamed", 2),
            CancellationToken.None);

        result.ShouldNotBeNull();
        result.Title.ShouldBe("Renamed");
        result.Revision.ShouldBe(3);
    }

    [Fact]
    public async Task ShouldPassDeleteRevisionToRepository()
    {
        _repository.Setup(i => i.DeleteAsync(_projectId, _chatId, 4, CancellationToken.None))
            .ReturnsAsync(new ChatDeleteResult(true, 4));

        var result = await CreateInstance().DeleteAsync(
            _projectId.Value,
            _chatId.Value,
            4,
            CancellationToken.None);

        result.IsDeleted.ShouldBeTrue();
        result.Revision.ShouldBe(4);
    }

    [Fact]
    public async Task ShouldUseProvidedMessageIdForBranchRoot()
    {
        var messageId = Guid.Parse("019f0000-0000-7000-8000-000000000003");
        var chat = new ChatThread(_chatId, _projectId, "Chat", _now);
        _repository.Setup(i => i.GetAsync(_projectId, _chatId, CancellationToken.None))
            .ReturnsAsync(new StoredChat(chat, 1));
        _clock.SetupGet(i => i.UtcNow).Returns(_now.AddMinutes(1));
        _repository.Setup(i => i.SaveAsync(chat, 1, CancellationToken.None))
            .ReturnsAsync(ChatSaveResult.Saved(2));

        var result = await CreateInstance().AppendMessageAsync(
            _projectId.Value,
            _chatId.Value,
            new AppendChatMessageRequest(messageId, null, "User", "Branch", 1),
            CancellationToken.None);

        result.ShouldNotBeNull();
        result.Messages.ShouldHaveSingleItem().Id.ShouldBe(messageId);
        _idGenerator.Verify(i => i.Create(), Times.Never);
    }

    private ChatService CreateInstance() => new(_repository.Object, _idGenerator.Object, _clock.Object);
}
