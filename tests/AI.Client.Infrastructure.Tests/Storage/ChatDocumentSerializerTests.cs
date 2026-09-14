namespace AI.Client.Infrastructure.Tests.Storage;

using Domain.Chats;
using AI.Client.Domain.Projects;
using AI.Client.Infrastructure.Storage;
using Shouldly;
using Xunit;

public class ChatDocumentSerializerTests
{
    [Fact]
    public void ShouldReadSummaryWithoutMaterializingMessages()
    {
        var createdAt = new DateTimeOffset(2026, 8, 13, 10, 0, 0, TimeSpan.Zero);
        var chat = new ChatThread(new ChatId(Guid.CreateVersion7()), new ProjectId(Guid.CreateVersion7()), "Chat", createdAt);
        chat.AddMessage(new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.User, "Question", createdAt), createdAt);

        var summary = ChatDocumentSerializer.DeserializeSummary(ChatDocumentSerializer.Serialize(chat, 7));

        summary.Id.ShouldBe(chat.Id);
        summary.ProjectId.ShouldBe(chat.ProjectId);
        summary.Revision.ShouldBe(7);
        summary.Title.ShouldBe("Chat");
    }

    [Fact]
    public void ShouldRestoreBranchTitles()
    {
        var createdAt = new DateTimeOffset(2026, 8, 13, 10, 0, 0, TimeSpan.Zero);
        var chat = new ChatThread(new ChatId(Guid.CreateVersion7()), new ProjectId(Guid.CreateVersion7()), "Chat", createdAt);
        var message = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.User, "Question", createdAt);
        chat.AddMessage(message, createdAt, message.Id.Value);
        chat.RenameBranch(message.Id, "Alternative", createdAt);

        var restored = ChatDocumentSerializer.Deserialize(ChatDocumentSerializer.Serialize(chat, 2));

        restored.Chat.Branches.Single(branch => branch.Id == message.Id.Value).Title.ShouldBe("Alternative");
    }

    [Fact]
    public void ShouldRestoreBranchMessageParents()
    {
        // Given
        var createdAt = new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);
        var chat = new ChatThread(new ChatId(Guid.CreateVersion7()), new ProjectId(Guid.CreateVersion7()), "Chat", createdAt);
        var parent = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.User, "Question", createdAt);
        var child = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), parent.Id, ChatMessageRole.Assistant, "Answer", createdAt);
        chat.AddMessage(parent, createdAt);
        chat.AddMessage(child, createdAt);

        // When
        var restored = ChatDocumentSerializer.Deserialize(ChatDocumentSerializer.Serialize(chat, 3));

        // Then
        restored.Revision.ShouldBe(3);
        restored.Chat.GetBranch(child.Id).Select(item => item.Content).ShouldBe(["Question", "Answer"]);
    }

    [Fact]
    public void ShouldRestoreSelectedEndpointProfile()
    {
        var createdAt = new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);
        var endpointId = new ConnectionId(Guid.Parse("019f0000-0000-7000-8000-000000000020"));
        var chat = new ChatThread(
            new ChatId(Guid.CreateVersion7()),
            new ProjectId(Guid.CreateVersion7()),
            "Chat",
            createdAt,
            endpointId);

        var restored = ChatDocumentSerializer.Deserialize(ChatDocumentSerializer.Serialize(chat, 1));

        restored.Chat.ConnectionId.ShouldBe(endpointId);
    }

    [Fact]
    public void ShouldRestoreIncompleteMessageState()
    {
        var createdAt = new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);
        var chat = new ChatThread(new ChatId(Guid.CreateVersion7()), new ProjectId(Guid.CreateVersion7()), "Chat", createdAt);
        chat.AddMessage(
            new ChatMessage(
                new ChatMessageId(Guid.CreateVersion7()),
                null,
                ChatMessageRole.Assistant,
                "Partial response",
                createdAt,
                true),
            createdAt);

        var restored = ChatDocumentSerializer.Deserialize(ChatDocumentSerializer.Serialize(chat, 1));

        restored.Chat.Messages.ShouldHaveSingleItem().IsIncomplete.ShouldBeTrue();
    }
}
