namespace AI.Client.Domain.Tests.Chats;

using AI.Client.Domain.Chats;
using AI.Client.Domain.Projects;
using Shouldly;
using Xunit;

public class ChatThreadTests
{
    [Fact]
    public void ShouldDeleteOnlyRequestedBranchAndReparentChildren()
    {
        var chat = CreateChat();
        var root = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.User, "Root", _now);
        var branchId = Guid.CreateVersion7();
        var branch = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), root.Id, ChatMessageRole.User, "Branch", _now);
        var childId = Guid.CreateVersion7();
        var child = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), branch.Id, ChatMessageRole.User, "Child", _now);
        var abandoned = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), branch.Id, ChatMessageRole.Assistant, "Abandoned", _now);
        chat.AddMessage(root, _now);
        chat.AddMessage(branch, _now, branchId, chat.Id.Value);
        chat.AddMessage(child, _now, childId, branchId);
        chat.AddMessage(abandoned, _now, branchId);

        var parent = chat.DeleteBranch(branchId, _now);
        chat.PruneUnreachableMessages([]);

        parent.ParentBranchId.ShouldBe(chat.Id.Value);
        chat.Branches.Select(item => item.Id).ShouldBe([chat.Id.Value, childId], ignoreOrder: true);
        chat.Branches.Single(item => item.Id == childId).ParentBranchId.ShouldBe(chat.Id.Value);
        chat.GetBranch(child.Id).Select(item => item.Content).ShouldBe(["Root", "Branch", "Child"]);
        chat.Messages.ShouldNotContain(message => message.Id == abandoned.Id);
    }

    [Fact]
    public void ShouldRenameBranch()
    {
        var chat = CreateChat();
        var message = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.User, "Question", _now);
        chat.AddMessage(message, _now, message.Id.Value);

        chat.RenameBranch(message.Id, "  Alternative  ", _now);

        chat.Branches.Single(branch => branch.Id == message.Id.Value).Title.ShouldBe("Alternative");
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
        // ReSharper disable once ConvertToLocalFunction
        var action = () => chat.AddMessage(message, _now, message.Id.Value);

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

        // ReSharper disable once ConvertToLocalFunction
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
