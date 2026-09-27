namespace AI.Application.Tests.Runs;

using AI.Application.Runs;
using AI.Application.Resources;
using AI.Contracts.Chat;
using AI.Contracts.Chats;
using AI.Contracts.Tools;
using AI.Contracts.Resources;
using Shouldly;
using Xunit;

public sealed class ChatContextTests
{
    private static readonly ToolResultCodec ToolResults = new(new ToolResultModelProjector());

    [Fact]
    public void ShouldExposeReferencesToModelWithoutChangingStoredText()
    {
        var id = Guid.CreateVersion7();
        var reference = new ChatResourceRef(Guid.CreateVersion7(), ChatResourceKind.File, "C:\\work\\code.cs");
        var chat = new ChatDetails(Guid.CreateVersion7(), Guid.CreateVersion7(), "Chat",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, null,
            [new ChatMessageView(id, null, "User", "Check this", DateTimeOffset.UnixEpoch,
                Resources: [reference])]);

        var message = new ChatContext(ToolResults, new ResourceModelProjection()).Build(chat, id).Single();

        message.Content.ShouldBe("Check this");
        message.ForModel.ShouldContain("file:");
        message.ForModel.ShouldContain("code.cs");
        message.ForModel.ShouldContain("contents not loaded");
    }

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

        var context = new ChatContext(ToolResults, new ResourceModelProjection()).Build(chat, nextUser);

        context.Select(message => message.Role).ShouldBe(["user", "assistant", "tool", "tool", "tool", "user"]);
        context.Where(message => message.Role == "tool").Select(message => message.ToolCallId)
            .ShouldBe(["call-1", "call-2", "call-3"]);
    }

    [Fact]
    public void ShouldRestoreModelProjectionForPersistedToolResults()
    {
        var user = Guid.NewGuid();
        var assistant = Guid.NewGuid();
        var tool = Guid.NewGuid();
        const string stored = """
            {"content":[{"type":"text","text":"large presentation payload"}],"structuredContent":{"exitCode":0},"_meta":{"instruction":"do not expose"},"isError":false}
            """;
        var chat = new ChatDetails(Guid.NewGuid(), Guid.NewGuid(), "Chat", DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, 1, null,
            [
                new ChatMessageView(user, null, "User", "Run", DateTimeOffset.UnixEpoch),
                new ChatMessageView(assistant, user, "Assistant", "", DateTimeOffset.UnixEpoch,
                    ToolCalls: [new ChatToolCall("call-1", "tool", "{}")]),
                new ChatMessageView(tool, assistant, "Tool", stored, DateTimeOffset.UnixEpoch, ToolCallId: "call-1")
            ]);

        var restored = new ChatContext(ToolResults, new ResourceModelProjection()).Build(chat, tool).Single(message => message.Role == "tool");

        restored.Content.ShouldBe(stored);
        restored.ForModel.ShouldBe("{\"exitCode\":0}");
        restored.ForModel.ShouldNotContain("do not expose");
        restored.ForModel.ShouldNotContain("large presentation payload");
    }

    [Theory]
    [InlineData("legacy plain-text result")]
    [InlineData("{\"unexpected\":true}")]
    public void ShouldKeepUnknownToolResultVerbatim(string stored)
    {
        var assistant = Guid.NewGuid();
        var tool = Guid.NewGuid();
        var chat = new ChatDetails(Guid.NewGuid(), Guid.NewGuid(), "Chat", DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, 1, null,
            [
                new ChatMessageView(assistant, null, "Assistant", "", DateTimeOffset.UnixEpoch,
                    ToolCalls: [new ChatToolCall("call-1", "tool", "{}")]),
                new ChatMessageView(tool, assistant, "Tool", stored, DateTimeOffset.UnixEpoch, ToolCallId: "call-1")
            ]);

        var restored = new ChatContext(ToolResults, new ResourceModelProjection()).Build(chat, tool).Single(message => message.Role == "tool");

        restored.ModelContent.ShouldBeNull();
        restored.ForModel.ShouldBe(stored);
    }
}
