using AI.Client.Domain.Chats;
using AI.Client.Domain.Projects;
using Shouldly;
using Xunit;

namespace AI.Client.Domain.Tests.Chats;

public class ChatThreadTests
{
    [Fact]
    public void ShouldDeleteBranchAndAllDescendants()
    {
        var chat = CreateChat();
        var root = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.User, "Root", _now);
        var branch = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), root.Id, ChatMessageRole.User, "Branch", _now);
        var reply = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), branch.Id, ChatMessageRole.Assistant, "Reply", _now);
        chat.AddMessage(root, _now);
        chat.AddMessage(branch, _now);
        chat.AddMessage(reply, _now);
        chat.RenameBranch(branch.Id, "Named branch", _now);

        var parentId = chat.DeleteBranch(branch.Id, _now);

        parentId.ShouldBe(root.Id);
        chat.Messages.ShouldHaveSingleItem().Id.ShouldBe(root.Id);
        chat.BranchTitles.ShouldBeEmpty();
    }

    [Fact]
    public void ShouldRenameBranch()
    {
        var chat = CreateChat();
        var message = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.User, "Question", _now);
        chat.AddMessage(message, _now);

        chat.RenameBranch(message.Id, "  Alternative  ", _now);

        chat.BranchTitles[message.Id].ShouldBe("Alternative");
    }

    private readonly DateTimeOffset _now = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShouldReturnMessagesOnSelectedBranchOnly()
    {
        // Given
        var chat = CreateChat();
        var root = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.User, "Question", _now);
        var firstReply = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), root.Id, ChatMessageRole.Assistant, "First", _now);
        var secondReply = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), root.Id, ChatMessageRole.Assistant, "Second", _now);
        chat.AddMessage(root, _now);
        chat.AddMessage(firstReply, _now);
        chat.AddMessage(secondReply, _now);

        // When
        var branch = chat.GetBranch(secondReply.Id);

        // Then
        branch.Select(item => item.Content).ShouldBe(["Question", "Second"]);
    }

    [Fact]
    public void ShouldRejectMessageWithUnknownParent()
    {
        // Given
        var chat = CreateChat();
        var message = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), new ChatMessageId(Guid.CreateVersion7()), ChatMessageRole.User, "Question", _now);

        // When
        var action = () => chat.AddMessage(message, _now);

        // Then
        Should.Throw<Exception>(action);
    }

    [Fact]
    public void ShouldTrimRenamedChatTitle()
    {
        var chat = CreateChat();

        chat.Rename("  Renamed chat  ", _now.AddMinutes(1));

        chat.Title.ShouldBe("Renamed chat");
        chat.UpdatedAt.ShouldBe(_now.AddMinutes(1));
    }

    [Fact]
    public void ShouldRejectEmptyChatTitle()
    {
        var chat = CreateChat();

        var action = () => chat.Rename(" ", _now.AddMinutes(1));

        Should.Throw<Exception>(action);
        chat.Title.ShouldBe("Chat");
    }

    private ChatThread CreateChat() => new(
        new ChatId(Guid.CreateVersion7()),
        new ProjectId(Guid.CreateVersion7()),
        "Chat",
        _now);
}
