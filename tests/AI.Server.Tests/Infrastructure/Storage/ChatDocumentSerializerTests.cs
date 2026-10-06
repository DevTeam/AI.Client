namespace AI.Infrastructure.Tests.Storage;

using Domain.Chats;
using AI.Domain.Projects;
using AI.Infrastructure.Storage;
using AI.Domain.Resources;
using Shouldly;
using System.Text.Json.Nodes;
using System.Text.Json;
using Xunit;

public class ChatDocumentSerializerTests
{
    private readonly ChatDocumentSerializer _serializer = new();

    [Fact]
    public void ShouldPreserveGuideStateAndMigrateLegacyKinds()
    {
        var chat = new ChatThread(new ChatId(Guid.NewGuid()), new ProjectId(Guid.NewGuid()),
            "Guide", DateTimeOffset.UnixEpoch, kind: ChatKind.Guide,
            kindState: JsonSerializer.SerializeToElement(new { mode = "click" }));
        var json = _serializer.Serialize(chat, 1);
        var written = JsonNode.Parse(json)!.AsObject();
        written.ContainsKey("IsGuide").ShouldBeFalse();
        written.ContainsKey("GuideMode").ShouldBeFalse();
        var restored = _serializer.Deserialize(json).Chat;
        restored.Kind.ShouldBe(ChatKind.Guide);
        restored.KindState!.Value.GetProperty("mode").GetString().ShouldBe("click");
        _serializer.DeserializeSummary(_serializer.SerializeSummary(chat, 1)).Kind.ShouldBe(ChatKind.Guide);
        var old = JsonNode.Parse(json)!.AsObject();
        old["SchemaVersion"] = 8;
        old.Remove("Kind");
        old.Remove("KindState");
        old["IsGuide"] = true;
        old["GuideMode"] = "click";
        _serializer.Deserialize(old.ToJsonString()).Chat.Kind.ShouldBe(ChatKind.Guide);
        old["IsGuide"] = false;
        old["GuideMode"] = "demo";
        _serializer.Deserialize(old.ToJsonString()).Chat.Kind.ShouldBe(ChatKind.Demo);
        old.Remove("GuideMode");
        _serializer.Deserialize(old.ToJsonString()).Chat.Kind.ShouldBe(ChatKind.Conversation);
    }

    [Fact]
    public void ShouldPreserveAutomaticTitleEligibilityAndStopAfterManualRename()
    {
        var now = DateTimeOffset.UnixEpoch;
        var chat = new ChatThread(new ChatId(Guid.CreateVersion7()), new ProjectId(Guid.CreateVersion7()),
            "First message", now, autoTitlePending: true);

        var restored = _serializer.Deserialize(_serializer.Serialize(chat, 1)).Chat;
        restored.AutoTitlePending.ShouldBeTrue();
        restored.Rename("My title", now);
        restored.ApplyAutomaticTitle("Suggested title", now).ShouldBeFalse();
        restored.Title.ShouldBe("My title");
        _serializer.Deserialize(_serializer.Serialize(restored, 2)).Chat.AutoTitlePending.ShouldBeFalse();
    }

    [Fact]
    public void ShouldPreserveApprovalModeAndReadOlderChatsAsAsk()
    {
        var now = DateTimeOffset.UnixEpoch;
        var chat = new ChatThread(new ChatId(Guid.CreateVersion7()), new ProjectId(Guid.CreateVersion7()), "Chat", now);
        chat.SetApprovalMode(ChatApprovalMode.FullAccess, now);

        var json = _serializer.Serialize(chat, 1);
        _serializer.Deserialize(json).Chat.ApprovalMode.ShouldBe(ChatApprovalMode.FullAccess);

        var older = JsonNode.Parse(json)!.AsObject();
        older.Remove("ApprovalMode").ShouldBeTrue();
        _serializer.Deserialize(older.ToJsonString()).Chat.ApprovalMode.ShouldBe(ChatApprovalMode.Ask);
    }

    [Fact]
    public void ShouldKeepResourceOnlyUserMessageAsReferences()
    {
        var now = DateTimeOffset.UnixEpoch;
        var reference = new ChatResource(Guid.CreateVersion7(), ChatResourceKind.Directory, "C:\\work\\src");
        var chat = new ChatThread(new ChatId(Guid.CreateVersion7()), new ProjectId(Guid.CreateVersion7()), "Chat", now);
        chat.AddMessage(new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), null,
            ChatMessageRole.User, string.Empty, now, resources: [reference]), now);

        var restored = _serializer.Deserialize(_serializer.Serialize(chat, 1));

        var message = restored.Chat.Messages.ShouldHaveSingleItem();
        message.Content.ShouldBe(string.Empty);
        message.Resources!.ShouldHaveSingleItem().ShouldBe(reference);
    }

    [Fact]
    public void ShouldRoundTripPinOrderActivityAndEmptiness()
    {
        var now = DateTimeOffset.UnixEpoch;
        var chat = new ChatThread(new ChatId(Guid.CreateVersion7()), new ProjectId(Guid.CreateVersion7()), "Chat", now);

        _serializer.DeserializeSummary(_serializer.SerializeSummary(chat, 1)).IsEmpty.ShouldBeTrue();

        chat.AddMessage(new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), null,
            ChatMessageRole.User, "Question", now.AddMinutes(1)), now.AddMinutes(1));
        chat.Pin("V", now.AddMinutes(2));
        // A failed run leaves no message behind, so only the document remembers this moment.
        chat.MarkActivity(now.AddMinutes(3));

        var restored = _serializer.Deserialize(_serializer.Serialize(chat, 2)).Chat;
        var summary = _serializer.DeserializeSummary(_serializer.SerializeSummary(chat, 2));

        restored.PinOrder.ShouldBe("V");
        restored.LastActivityAt.ShouldBe(now.AddMinutes(3));
        summary.PinOrder.ShouldBe("V");
        summary.IsEmpty.ShouldBeFalse();
        summary.LastActivityAt.ShouldBe(now.AddMinutes(3));
    }

    [Fact]
    public void RemovingAReviewLinkShouldKeepTheMessageAndOtherReferencesAfterReload()
    {
        var now = DateTimeOffset.UnixEpoch;
        var review = new ChatResource(Guid.CreateVersion7(), ChatResourceKind.Review, string.Empty, "API review");
        var file = new ChatResource(Guid.CreateVersion7(), ChatResourceKind.File, "C:\\work\\api.cs");
        var chat = new ChatThread(new ChatId(Guid.CreateVersion7()), new ProjectId(Guid.CreateVersion7()), "Chat", now);
        var messageId = new ChatMessageId(Guid.CreateVersion7());
        chat.AddMessage(new ChatMessage(messageId, null, ChatMessageRole.User, string.Empty, now,
            resources: [review, file]), now);

        chat.RemoveReviewReferences(review.Id, now).ShouldBeTrue();
        var restored = _serializer.Deserialize(_serializer.Serialize(chat, 2));

        var message = restored.Chat.Messages.ShouldHaveSingleItem();
        message.Resources!.ShouldHaveSingleItem().ShouldBe(file);
        message.Content.ShouldBe(string.Empty);
    }

    [Fact]
    public void RemovingAReviewShouldClearItsLinksFromEveryMessage()
    {
        var now = DateTimeOffset.UnixEpoch;
        var review = new ChatResource(Guid.CreateVersion7(), ChatResourceKind.Review, string.Empty, "Review");
        var file = new ChatResource(Guid.CreateVersion7(), ChatResourceKind.File, "C:\\work\\api.cs");
        var chat = new ChatThread(new ChatId(Guid.CreateVersion7()), new ProjectId(Guid.CreateVersion7()), "Chat", now);
        var first = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.User,
            string.Empty, now, resources: [review]);
        var second = new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), first.Id, ChatMessageRole.User,
            string.Empty, now, resources: [review, file]);
        chat.AddMessage(first, now);
        chat.AddMessage(second, now);

        chat.RemoveReviewReferences(review.Id, now).ShouldBeTrue();
        var restored = _serializer.Deserialize(_serializer.Serialize(chat, 3));
        restored.Chat.Messages.Single(item => item.Id == first.Id).Resources.ShouldBeEmpty();
        restored.Chat.Messages.Single(item => item.Id == second.Id).Resources!.ShouldHaveSingleItem().ShouldBe(file);
    }

    [Fact]
    public void ShouldRestoreWorkspaceChangesAttachedToAMessage()
    {
        var createdAt = new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
        var chat = new ChatThread(new ChatId(Guid.CreateVersion7()), new ProjectId(Guid.CreateVersion7()), "Chat", createdAt);
        var changes = new ChatWorkspaceChangeSet(
            [new ChatFileChange("src/file.cs", ChatFileChangeKind.Modified, 4, 2,
                Diff: "@@ -1,1 +1,1 @@\n-old\n+new", Confidence: ChatFileChangeConfidence.Measured)],
            4,
            2,
            Guid.CreateVersion7());
        chat.AddMessage(new ChatMessage(
            new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.Assistant, "Done", createdAt,
            workspaceChanges: changes), createdAt);

        var restored = _serializer.Deserialize(_serializer.Serialize(chat, 3));

        var restoredChanges = restored.Chat.Messages.ShouldHaveSingleItem().WorkspaceChanges!;
        restoredChanges.Additions.ShouldBe(4);
        restoredChanges.Deletions.ShouldBe(2);
        restoredChanges.UndoId.ShouldBe(changes.UndoId);
        restoredChanges.Files.ShouldHaveSingleItem().Diff.ShouldBe("@@ -1,1 +1,1 @@\n-old\n+new");
    }

    [Fact]
    public void ShouldReadPreviousSchemaWithoutWorkspaceChanges()
    {
        var createdAt = new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
        var chat = new ChatThread(new ChatId(Guid.CreateVersion7()), new ProjectId(Guid.CreateVersion7()), "Chat", createdAt);
        chat.AddMessage(new ChatMessage(
            new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.Assistant, "Done", createdAt), createdAt);
        var previous = _serializer.Serialize(chat, 1)
            .Replace("\"SchemaVersion\": 6", "\"SchemaVersion\": 5", StringComparison.Ordinal);

        var restored = _serializer.Deserialize(previous);

        restored.Chat.Messages.ShouldHaveSingleItem().WorkspaceChanges.ShouldBeNull();
    }

    [Fact]
    public void ShouldReadSummaryWithoutMaterializingMessages()
    {
        var createdAt = new DateTimeOffset(2026, 8, 13, 10, 0, 0, TimeSpan.Zero);
        var chat = new ChatThread(new ChatId(Guid.CreateVersion7()), new ProjectId(Guid.CreateVersion7()), "Chat", createdAt);
        chat.AddMessage(new ChatMessage(new ChatMessageId(Guid.CreateVersion7()), null, ChatMessageRole.User, "Question", createdAt), createdAt);

        var previous = _serializer.Serialize(chat, 7)
            .Replace("\"SchemaVersion\": 6", "\"SchemaVersion\": 5", StringComparison.Ordinal);
        var summary = _serializer.DeserializeSummary(previous);

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

        var restored = _serializer.Deserialize(_serializer.Serialize(chat, 2));

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
        var restored = _serializer.Deserialize(_serializer.Serialize(chat, 3));

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

        var restored = _serializer.Deserialize(_serializer.Serialize(chat, 1));

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

        var restored = _serializer.Deserialize(_serializer.Serialize(chat, 1));

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

        var summary = _serializer.DeserializeSummary(_serializer.SerializeSummary(chat, 4));

        summary.BranchCount.ShouldBe(2);
        summary.HasCurrentManifest.ShouldBeTrue();
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
        var document = JsonNode.Parse(_serializer.SerializeSummary(chat, 1))!.AsObject();
        document.ShouldContainKey("BranchCount");
        document.Remove("BranchCount");
        var withoutCount = document.ToJsonString();

        var summary = _serializer.DeserializeSummary(withoutCount);

        summary.BranchCount.ShouldBe(0);
        summary.HasCurrentManifest.ShouldBeFalse();
        summary.Title.ShouldBe("Chat");
    }
}
