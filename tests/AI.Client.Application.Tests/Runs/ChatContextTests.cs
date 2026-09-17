namespace AI.Client.Application.Tests.Runs;

using AI.Client.Application.Runs;
using AI.Client.Contracts.Chat;
using AI.Client.Contracts.Chats;
using Shouldly;
using Xunit;

public sealed class ChatContextTests
{
    [Fact]
    public void ShouldRepairInterruptedToolBatchInOriginalCallOrder()
    {
        var user = Guid.NewGuid();
        var assistant = Guid.NewGuid();
        var firstResult = Guid.NewGuid();
        var nextUser = Guid.NewGuid();
        var calls = new[]
        {
            new ChatToolCall("call-1", "tool", "{}"),
            new ChatToolCall("call-2", "tool", "{}"),
            new ChatToolCall("call-3", "tool", "{}")
        };
        var chat = new ChatDetails(Guid.NewGuid(), Guid.NewGuid(), "Chat", DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, 1, null,
            [
                new ChatMessageView(user, null, "User", "Start", DateTimeOffset.UnixEpoch),
                new ChatMessageView(assistant, user, "Assistant", "", DateTimeOffset.UnixEpoch, ToolCalls: calls),
                new ChatMessageView(firstResult, assistant, "Tool", "first result", DateTimeOffset.UnixEpoch, ToolCallId: "call-1"),
                new ChatMessageView(nextUser, firstResult, "User", "Urgent", DateTimeOffset.UnixEpoch)
            ]);

        var context = ChatContext.Get(chat, nextUser);

        context.Select(message => message.Role).ShouldBe(["user", "assistant", "tool", "tool", "tool", "user"]);
        context.Where(message => message.Role == "tool").Select(message => message.ToolCallId)
            .ShouldBe(["call-1", "call-2", "call-3"]);
    }
}
