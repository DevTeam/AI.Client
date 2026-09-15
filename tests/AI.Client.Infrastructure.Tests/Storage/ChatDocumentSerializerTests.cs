namespace AI.Client.Infrastructure.Tests.Storage;

using Domain.Chats;
using AI.Client.Domain.Projects;
using AI.Client.Infrastructure.Storage;
using Shouldly;
using System.Text.Json.Nodes;
using Xunit;

public class ChatDocumentSerializerTests
{
    [Fact]
    public void ShouldRestoreWorkspaceChangesAttachedToAMessage()
    {
        var createdAt = new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
        var chat = new ChatThread(new ChatId(Guid.CreateVersion7()), new ProjectId(Guid.CreateVersion7()), "Chat", createdAt);
        var changes = new ChatWorkspaceChangeSet(
            [new ChatFileChange("src/file.cs", ChatFileChangeKind.Modified, 4, 2,
                Diff: "@@ -1,1 +1,1 @@\n-old\n+new", Confidence: ChatFileChangeConfidence.Measured)],
            4,
            2);
        chat.AddMessage(new ChatMessage(
            new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.Assistant, "Done", createdAt,
            workspaceChanges: changes), createdAt);

        var restored = ChatDocumentSerializer.Deserialize(ChatDocumentSerializer.Serialize(chat, 3));

        var restoredChanges = restored.Chat.Messages.ShouldHaveSingleItem().WorkspaceChanges!;
        restoredChanges.Additions.ShouldBe(4);
        restoredChanges.Deletions.ShouldBe(2);
        restoredChanges.Files.ShouldHaveSingleItem().Diff.ShouldBe("@@ -1,1 +1,1 @@\n-old\n+new");
    }

    [Fact]
    public void ShouldReadPreviousSchemaWithoutWorkspaceChanges()
    {
        var createdAt = new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
        var chat = new ChatThread(new ChatId(Guid.CreateVersion7()), new ProjectId(Guid.CreateVersion7()), "Chat", createdAt);
        chat.AddMessage(new ChatMessage(
            new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.Assistant, "Done", createdAt), createdAt);
        var previous = ChatDocumentSerializer.Serialize(chat, 1)
            .Replace("\"SchemaVersion\": 6", "\"SchemaVersion\": 5", StringComparison.Ordinal);

        var restored = ChatDocumentSerializer.Deserialize(previous);

        restored.Chat.Messages.ShouldHaveSingleItem().WorkspaceChanges.ShouldBeNull();
    }

    [Fact]
    public void ShouldReadSummaryWithoutMaterializingMessages()
    {
        var createdAt = new DateTimeOffset(2026, 8, 13, 10, 0, 0, TimeSpan.Zero);
        var chat = new ChatThread(new ChatId(Guid.CreateVersion7()), new ProjectId(Guid.CreateVersion7()), "Chat", createdAt);
        chat.AddMessage(new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.User, "Question", createdAt), createdAt);

        var previous = ChatDocumentSerializer.Serialize(chat, 7)
            .Replace("\"SchemaVersion\": 6", "\"SchemaVersion\": 5", StringComparison.Ordinal);
        var summary = ChatDocumentSerializer.DeserializeSummary(previous);

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

    [Fact]
    public void ShouldRoundTripBranchCountThroughTheSummary()
    {
        var createdAt = new DateTimeOffset(2026, 8, 13, 10, 0, 0, TimeSpan.Zero);
        var chat = new ChatThread(new ChatId(Guid.CreateVersion7()), new ProjectId(Guid.CreateVersion7()), "Chat", createdAt);
        var root = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.User, "Question", createdAt);
        chat.AddMessage(root, createdAt);
        // Two alternative branches off the root, each with its own head message. A fork is stored
        // under the id of its own first new message and hangs off the main branch, which is
        // identified by the chat id.
        var firstHead = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), root.Id, ChatMessageRole.Assistant, "First", createdAt);
        var secondHead = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), root.Id, ChatMessageRole.Assistant, "Second", createdAt);
        chat.AddMessage(firstHead, createdAt, firstHead.Id.Value, chat.Id.Value);
        chat.AddMessage(secondHead, createdAt, secondHead.Id.Value, chat.Id.Value);

        var summary = ChatDocumentSerializer.DeserializeSummary(ChatDocumentSerializer.SerializeSummary(chat, 4));

        summary.BranchCount.ShouldBe(2);
    }

    [Fact]
    public void ShouldNotCountTheMainBranchOrABranchWithoutAHead()
    {
        var createdAt = new DateTimeOffset(2026, 8, 13, 10, 0, 0, TimeSpan.Zero);
        var chat = new ChatThread(new ChatId(Guid.CreateVersion7()), new ProjectId(Guid.CreateVersion7()), "Chat", createdAt);
        var root = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.User, "Question", createdAt);
        chat.AddMessage(root, createdAt);

        // A chat with messages but no forks still has its own main branch, which is not an
        // alternative and must never be advertised as one.
        chat.BranchCount.ShouldBe(0);

        // A branch whose head is not set yet (an empty fork) has no row in the branch tree, so
        // counting it would advertise a branch the chat list cannot open.
        chat.RestoreBranches(
        [
            new ChatBranch(chat.Id.Value, root.Id, "Chat"),
            new ChatBranch(Guid.CreateVersion7(), null, "Empty fork", chat.Id.Value)
        ]);

        chat.BranchCount.ShouldBe(0);
    }

    [Fact]
    public void ShouldReadSummaryWrittenBeforeBranchCountExisted()
    {
        var createdAt = new DateTimeOffset(2026, 8, 13, 10, 0, 0, TimeSpan.Zero);
        var chat = new ChatThread(new ChatId(Guid.CreateVersion7()), new ProjectId(Guid.CreateVersion7()), "Chat", createdAt);

        // A pre-existing summary file has no BranchCount property at all; it must deserialize to
        // zero rather than throw, so an older data directory keeps listing its chats. The property
        // is removed through the JSON DOM rather than by string replacement, which would depend on
        // the writer's indentation and line endings.
        var document = JsonNode.Parse(ChatDocumentSerializer.SerializeSummary(chat, 1))!.AsObject();
        document.ShouldContainKey("BranchCount");
        document.Remove("BranchCount");
        var withoutCount = document.ToJsonString();

        var summary = ChatDocumentSerializer.DeserializeSummary(withoutCount);

        summary.BranchCount.ShouldBe(0);
        summary.Title.ShouldBe("Chat");
    }
}
