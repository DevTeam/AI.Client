using AI.Client.Domain.Chats;
using AI.Client.Domain.Projects;
using AI.Client.Infrastructure.Storage;
using Shouldly;
using Xunit;

namespace AI.Client.Infrastructure.Tests.Storage;

public class ChatDocumentSerializerTests
{
    [Fact]
    public void ShouldRestoreBranchTitles()
    {
        var createdAt = new DateTimeOffset(2026, 8, 13, 10, 0, 0, TimeSpan.Zero);
        var chat = new ChatThread(new ChatId(Guid.CreateVersion7()), new ProjectId(Guid.CreateVersion7()), "Chat", createdAt);
        var message = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.User, "Question", createdAt);
        chat.AddMessage(message, createdAt);
        chat.RenameBranch(message.Id, "Alternative", createdAt);

        var serializer = new ChatDocumentSerializer();
        var restored = serializer.Deserialize(serializer.Serialize(chat, 2));

        restored.Chat.BranchTitles[message.Id].ShouldBe("Alternative");
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
        var serializer = new ChatDocumentSerializer();

        // When
        var restored = serializer.Deserialize(serializer.Serialize(chat, 3));

        // Then
        restored.Revision.ShouldBe(3);
        restored.Chat.GetBranch(child.Id).Select(item => item.Content).ShouldBe(["Question", "Answer"]);
    }

    [Fact]
    public void ShouldRestoreSelectedEndpointProfile()
    {
        var createdAt = new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);
        var endpointId = new EndpointProfileId(Guid.Parse("019f0000-0000-7000-8000-000000000020"));
        var chat = new ChatThread(
            new ChatId(Guid.CreateVersion7()),
            new ProjectId(Guid.CreateVersion7()),
            "Chat",
            createdAt,
            endpointId);
        var serializer = new ChatDocumentSerializer();

        var restored = serializer.Deserialize(serializer.Serialize(chat, 1));

        restored.Chat.EndpointProfileId.ShouldBe(endpointId);
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
        var serializer = new ChatDocumentSerializer();

        var restored = serializer.Deserialize(serializer.Serialize(chat, 1));

        restored.Chat.Messages.ShouldHaveSingleItem().IsIncomplete.ShouldBeTrue();
    }
}
