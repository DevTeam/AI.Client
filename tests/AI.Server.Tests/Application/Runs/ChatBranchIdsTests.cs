namespace AI.Application.Tests.Runs;

using AI.Application.Runs;
using AI.Application.Resources;
using AI.Contracts.Chats;
using AI.Contracts.Tools;
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
        new ChatBranchIds().Collect(chat).ShouldBe(new HashSet<Guid> { chatId, branchId }, ignoreOrder: true);
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
        var toolResults = new ToolResultCodec(new ToolResultModelProjector());
        new ChatContext(toolResults, new ResourceModelProjection(), new ModelMessageHeader()).Build(chat, second).Select(message => message.Content).ShouldBe(["Question", "Second"]);
    }
}
