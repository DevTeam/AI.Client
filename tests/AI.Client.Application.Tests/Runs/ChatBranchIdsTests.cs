using AI.Client.Application.Runs;
using AI.Client.Contracts.Chats;
using Shouldly;
using Xunit;

namespace AI.Client.Application.Tests.Runs;

public sealed class ChatBranchIdsTests
{
    [Fact]
    public void ShouldReturnChatIdAndOnlyCurrentAlternativeBranchRoots()
    {
        var chatId = Guid.NewGuid();
        var originalUser = Message(null, "User", 0);
        var originalAssistant = Message(originalUser.Id, "Assistant", 1);
        var branchUser = Message(originalAssistant.Id, "User", 2);
        var siblingBranchUser = Message(originalAssistant.Id, "User", 3);
        var nestedAssistant = Message(branchUser.Id, "Assistant", 4);

        var result = ChatBranchIds.Get(Chat(chatId, [originalUser, originalAssistant, branchUser, siblingBranchUser, nestedAssistant]));

        result.ShouldBe(new HashSet<Guid> { chatId, siblingBranchUser.Id }, ignoreOrder: true);
    }

    [Fact]
    public void ShouldDropFormerBranchRootWhenItsSiblingIsRemoved()
    {
        var chatId = Guid.NewGuid();
        var parent = Message(null, "Assistant", 0);
        var remainingUser = Message(parent.Id, "User", 1);

        var result = ChatBranchIds.Get(Chat(chatId, [parent, remainingUser]));

        result.ShouldBe(new HashSet<Guid> { chatId }, ignoreOrder: true);
    }

    private static ChatDetails Chat(Guid chatId, IReadOnlyList<ChatMessageView> messages) =>
        new(chatId, Guid.NewGuid(), "Chat", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, null, messages);

    private static ChatMessageView Message(Guid? parentId, string role, int minute) =>
        new(Guid.NewGuid(), parentId, role, role, DateTimeOffset.UnixEpoch.AddMinutes(minute), false);
}
