namespace AI.Web.Tests.ChatDeltas;

using AI.Contracts.Chats;
using AI.Contracts.Runs;
using AI.Web.Chats;
using Shouldly;
using Xunit;

public sealed class ChatMessageDeltaMergerTests
{
    private readonly ChatMessageDeltaMerger _merger = new();

    [Fact]
    public void ShouldApplyAContiguousTailAndAdvanceTheBranch()
    {
        var chatId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var first = Message(Guid.NewGuid(), null, "User", "start");
        var second = Message(Guid.NewGuid(), first.Id, "Assistant", "calling");
        var third = Message(Guid.NewGuid(), second.Id, "Tool", "result");
        var chat = Chat(chatId, branchId, 10, first);
        var run = Run(chatId, branchId, 12, third.Id, 4,
            new ChatMessageAppend(10, 11, second),
            new ChatMessageAppend(11, 12, third));

        _merger.TryApply(chat, run, out var updated).ShouldBeTrue();

        updated.Revision.ShouldBe(12);
        updated.Messages.Select(item => item.Id).ShouldBe([first.Id, second.Id, third.Id]);
        var branch = updated.Branches!.Single();
        branch.HeadMessageId.ShouldBe(third.Id);
        branch.Revision.ShouldBe(4);
    }

    [Fact]
    public void ShouldRejectATailWhenARevisionWasMissed()
    {
        var chatId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var first = Message(Guid.NewGuid(), null, "User", "start");
        var appended = Message(Guid.NewGuid(), first.Id, "Tool", "result");
        var chat = Chat(chatId, branchId, 10, first);
        var run = Run(chatId, branchId, 13, appended.Id, 2,
            new ChatMessageAppend(12, 13, appended));

        _merger.TryApply(chat, run, out var updated).ShouldBeFalse();
        updated.ShouldBeSameAs(chat);
    }

    private static ChatMessageView Message(Guid id, Guid? parentId, string role, string content) =>
        new(id, parentId, role, content, DateTimeOffset.UtcNow);

    private static ChatDetails Chat(Guid chatId, Guid branchId, long revision, ChatMessageView message) =>
        new(chatId, Guid.NewGuid(), "Chat", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, revision, null,
            [message], [new ChatBranchView(branchId, message.Id, "Main", Revision: 1)]);

    private static ChatRunSnapshot Run(Guid chatId, Guid branchId, long chatRevision, Guid head, long branchRevision,
        params ChatMessageAppend[] appends) =>
        new(Guid.NewGuid(), chatId, branchId, ChatRunStatus.Generating, "", [], false, null, 1,
            ChatRevision: chatRevision, HeadMessageId: head, BranchRevision: branchRevision,
            MessageDelta: new ChatMessageDelta(appends));
}
