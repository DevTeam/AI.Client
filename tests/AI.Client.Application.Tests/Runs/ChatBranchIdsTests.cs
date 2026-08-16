namespace AI.Client.Application.Tests.Runs;

using AI.Client.Application.Runs;
using AI.Client.Contracts.Chats;
using Shouldly;
using Xunit;

public sealed class ChatBranchIdsTests
{
    [Fact]
    public void ShouldKeepExplicitBranchIdsRegardlessOfSiblingOrder()
    {
        var chatId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var chat = new ChatDetails(chatId, Guid.NewGuid(), "Chat", DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, 1, null, [],
            [new ChatBranchView(chatId, null, "Main"), new ChatBranchView(branchId, null, "Alternative", chatId)]);
        ChatBranchIds.Get(chat).ShouldBe(new HashSet<Guid> { chatId, branchId }, ignoreOrder: true);
    }

    [Fact]
    public void ShouldExcludeSiblingMessagesFromModelContext()
    {
        var root = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var chat = new ChatDetails(Guid.NewGuid(), Guid.NewGuid(), "Chat", DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, 1, null,
            [new ChatMessageView(root, null, "User", "Question", DateTimeOffset.UnixEpoch),
             new ChatMessageView(first, root, "Assistant", "First", DateTimeOffset.UnixEpoch),
             new ChatMessageView(second, root, "Assistant", "Second", DateTimeOffset.UnixEpoch)]);
        ChatContext.Get(chat, second).Select(message => message.Content).ShouldBe(["Question", "Second"]);
    }
}
