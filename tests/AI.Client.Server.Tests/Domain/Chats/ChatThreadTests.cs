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
    public void ShouldRewindBranchAndLeaveTheAbandonedTailUnreachable()
    {
        var chat = CreateChat();
        var question = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.User, "Question", _now);
        var truncated = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), question.Id, ChatMessageRole.Assistant, "Half", _now, true);
        chat.AddMessage(question, _now);
        chat.AddMessage(truncated, _now);
        var revision = chat.Branches.Single(branch => branch.Id == chat.Id.Value).Revision;

        chat.RewindBranchTo(chat.Id.Value, question.Id, _now).ShouldBeTrue();
        chat.PruneUnreachableMessages([]);

        chat.Branches.Single(branch => branch.Id == chat.Id.Value).HeadMessageId.ShouldBe(question.Id);
        chat.Branches.Single(branch => branch.Id == chat.Id.Value).Revision.ShouldBe(revision + 1);
        chat.Messages.ShouldNotContain(message => message.Id == truncated.Id);
        chat.RewindBranchTo(chat.Id.Value, question.Id, _now).ShouldBeFalse();
    }

    [Fact]
    public void ShouldRefuseToRewindPastTheBranchRoot()
    {
        var chat = CreateChat();
        var root = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.User, "Root", _now);
        var branchId = Guid.CreateVersion7();
        var forked = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), root.Id, ChatMessageRole.User, "Forked", _now);
        chat.AddMessage(root, _now);
        chat.AddMessage(forked, _now, branchId, chat.Id.Value);

        // A branch whose root is no longer an ancestor of its head cannot be loaded back, so the
        // rewind is refused rather than written.
        Should.Throw<AI.Client.Domain.Common.DomainException>(() => chat.RewindBranchTo(branchId, root.Id, _now));
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

    [Fact]
    public void ShouldMoveBranchRootWhenTheRootMessageItselfIsReplaced()
    {
        var chat = CreateChat();
        var question = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.User, "Question", _now);
        chat.AddMessage(question, _now);
        var forkId = Guid.CreateVersion7();
        var forked = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), question.Id, ChatMessageRole.User, "Forked", _now);
        chat.AddMessage(forked, _now, forkId, chat.Id.Value);
        var replacement = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), question.Id, ChatMessageRole.User, "Edited", _now);

        chat.ReplaceInBranch(forkId, forked.Id, replacement, _now);

        var branch = chat.Branches.Single(item => item.Id == forkId);
        branch.HeadMessageId.ShouldBe(replacement.Id);
        branch.RootMessageId.ShouldBe(replacement.Id);
        // The invariant RestoreBranches enforces: a branch root has to stay an ancestor of its head.
        chat.GetBranch(branch.HeadMessageId).ShouldContain(message => message.Id == branch.RootMessageId);
    }

    [Fact]
    public void ShouldKeepBranchRootWhenAMessageBelowTheRootIsReplaced()
    {
        var chat = CreateChat();
        var question = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.User, "Question", _now);
        chat.AddMessage(question, _now);
        var forkId = Guid.CreateVersion7();
        var forked = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), question.Id, ChatMessageRole.User, "Forked", _now);
        chat.AddMessage(forked, _now, forkId, chat.Id.Value);
        var answer = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), forked.Id, ChatMessageRole.Assistant, "Answer", _now);
        chat.AddMessage(answer, _now, forkId);
        var replacement = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), forked.Id, ChatMessageRole.Assistant, "Redone", _now);

        chat.ReplaceInBranch(forkId, answer.Id, replacement, _now);

        var branch = chat.Branches.Single(item => item.Id == forkId);
        branch.HeadMessageId.ShouldBe(replacement.Id);
        branch.RootMessageId.ShouldBe(forked.Id);
    }

    [Fact]
    public void ShouldReloadAChatWhoseForkRootWasReplaced()
    {
        var chat = CreateChat();
        var question = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.User, "Question", _now);
        chat.AddMessage(question, _now);
        var forkId = Guid.CreateVersion7();
        var forked = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), question.Id, ChatMessageRole.User, "Forked", _now);
        chat.AddMessage(forked, _now, forkId, chat.Id.Value);
        var replacement = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), question.Id, ChatMessageRole.User, "Edited", _now);
        chat.ReplaceInBranch(forkId, forked.Id, replacement, _now);

        // RestoreBranches is the load-time gate that rejected the chat written by the old
        // ReplaceInBranch; feeding it the branches we just produced is the crash, reproduced.
        // ReSharper disable once ConvertToLocalFunction
        var action = () => chat.RestoreBranches(chat.Branches.ToArray());

        Should.NotThrow(action);
    }

    private ChatThread CreateChat() => new(
        new ChatId(Guid.CreateVersion7()),
        new ProjectId(Guid.CreateVersion7()),
        "Chat",
        _now);
}
