namespace AI.Application.Tests.Chats;

using AI.Application.Chats;
using AI.Application.Projects;
using AI.Contracts.Chats;
using AI.Domain.Chats;
using AI.Domain.Projects;
using Moq;
using Shouldly;
using Xunit;

public class ChatServiceTests
{
    private readonly Mock<IChatRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IIdGenerator> _idGenerator = new(MockBehavior.Strict);
    private readonly Mock<IClock> _clock = new(MockBehavior.Strict);
    private readonly ProjectId _projectId = new(Guid.Parse("019f0000-0000-7000-8000-000000000001"));
    private readonly ChatId _chatId = new(Guid.Parse("019f0000-0000-7000-8000-000000000002"));
    private readonly DateTimeOffset _now = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ShouldRenameChatUsingExpectedRevision()
    {
        var chat = new ChatThread(_chatId, _projectId, "Chat", _now);
        _repository.Setup(i => i.GetAsync(_projectId, _chatId, CancellationToken.None))
            .ReturnsAsync(new StoredChat(chat, 2));
        _clock.SetupGet(i => i.UtcNow).Returns(_now.AddMinutes(1));
        _repository.Setup(i => i.SaveAsync(chat, 2, CancellationToken.None))
            .ReturnsAsync(ChatSaveResult.Saved(3));

        var result = await CreateInstance().RenameAsync(
            _projectId.Value,
            _chatId.Value,
            new RenameChatRequest("Renamed", 2),
            CancellationToken.None);

        result.ShouldNotBeNull();
        result.Title.ShouldBe("Renamed");
        result.Revision.ShouldBe(3);
    }

    [Fact]
    public async Task ShouldPassDeleteRevisionToRepository()
    {
        _repository.Setup(i => i.DeleteAsync(_projectId, _chatId, 4, CancellationToken.None))
            .ReturnsAsync(new ChatDeleteResult(true, 4));

        var result = await CreateInstance().DeleteAsync(
            _projectId.Value,
            _chatId.Value,
            4,
            CancellationToken.None);

        result.IsDeleted.ShouldBeTrue();
        result.Revision.ShouldBe(4);
    }

    [Fact]
    public async Task ShouldUseProvidedMessageIdForBranchRoot()
    {
        var messageId = Guid.Parse("019f0000-0000-7000-8000-000000000003");
        var chat = new ChatThread(_chatId, _projectId, "Chat", _now);
        _repository.Setup(i => i.GetAsync(_projectId, _chatId, CancellationToken.None))
            .ReturnsAsync(new StoredChat(chat, 1));
        _clock.SetupGet(i => i.UtcNow).Returns(_now.AddMinutes(1));
        _repository.Setup(i => i.SaveAsync(chat, 1, CancellationToken.None))
            .ReturnsAsync(ChatSaveResult.Saved(2));

        var result = await CreateInstance().AppendMessageAsync(
            _projectId.Value,
            _chatId.Value,
            new AppendChatMessageRequest(messageId, null, "User", "Branch", 1),
            CancellationToken.None);

        result.ShouldNotBeNull();
        result.Messages.ShouldHaveSingleItem().Id.ShouldBe(messageId);
        _idGenerator.Verify(i => i.Create(), Times.Never);
    }

    [Fact]
    public async Task ShouldKeepNotesOfUnansweredTurnInTranscript()
    {
        var firstUserId = new ChatMessageId(Guid.Parse("019f0000-0000-7000-8000-000000000020"));
        var firstNoteId = new ChatMessageId(Guid.Parse("019f0000-0000-7000-8000-000000000021"));
        var firstResultId = new ChatMessageId(Guid.Parse("019f0000-0000-7000-8000-000000000022"));
        var firstAnswerId = new ChatMessageId(Guid.Parse("019f0000-0000-7000-8000-000000000023"));
        var userId = new ChatMessageId(Guid.Parse("019f0000-0000-7000-8000-000000000024"));
        var noteId = new ChatMessageId(Guid.Parse("019f0000-0000-7000-8000-000000000025"));
        var resultId = new ChatMessageId(Guid.Parse("019f0000-0000-7000-8000-000000000026"));
        var chat = new ChatThread(_chatId, _projectId, "Chat", _now);
        chat.AddMessage(new ChatMessage(firstUserId, null, ChatMessageRole.User, "First", _now), _now);
        chat.AddMessage(new ChatMessage(firstNoteId, firstUserId, ChatMessageRole.Assistant, "Earlier note", _now.AddSeconds(1),
            toolCalls: [new ChatToolCall("call-1", "process_run", "{}")]), _now.AddSeconds(1));
        chat.AddMessage(new ChatMessage(firstResultId, firstNoteId, ChatMessageRole.Tool, "result", _now.AddSeconds(2),
            toolCallId: "call-1"), _now.AddSeconds(2));
        chat.AddMessage(new ChatMessage(firstAnswerId, firstResultId, ChatMessageRole.Assistant, "Answer", _now.AddSeconds(3)),
            _now.AddSeconds(3));
        chat.AddMessage(new ChatMessage(userId, firstAnswerId, ChatMessageRole.User, "Second", _now.AddSeconds(4)), _now.AddSeconds(4));
        chat.AddMessage(new ChatMessage(noteId, userId, ChatMessageRole.Assistant, "Checking the build", _now.AddSeconds(5),
            toolCalls: [new ChatToolCall("call-2", "process_run", "{\"command\":\"large\"}")]), _now.AddSeconds(5));
        chat.AddMessage(new ChatMessage(resultId, noteId, ChatMessageRole.Tool, "very large result", _now.AddSeconds(6),
            toolCallId: "call-2"), _now.AddSeconds(6));
        _repository.Setup(i => i.GetAsync(_projectId, _chatId, CancellationToken.None))
            .ReturnsAsync(new StoredChat(chat, 3));

        var transcript = await CreateInstance().GetTranscriptAsync(_projectId.Value, _chatId.Value, CancellationToken.None);

        transcript.ShouldNotBeNull();
        var note = transcript.Messages.Single(message => message.Id == noteId.Value);
        note.Content.ShouldBe("Checking the build");
        note.ContentOmitted.ShouldBeFalse();
        note.ToolCalls!.Single().Arguments.ShouldBeEmpty();
        transcript.Messages.Single(message => message.Id == resultId.Value).ContentOmitted.ShouldBeTrue();
        transcript.Messages.Single(message => message.Id == firstNoteId.Value).ContentOmitted.ShouldBeTrue();
    }

    [Fact]
    public async Task ShouldLoadTranscriptActivityAndToolResultInLayers()
    {
        var userId = new ChatMessageId(Guid.Parse("019f0000-0000-7000-8000-000000000010"));
        var callId = new ChatMessageId(Guid.Parse("019f0000-0000-7000-8000-000000000011"));
        var resultId = new ChatMessageId(Guid.Parse("019f0000-0000-7000-8000-000000000012"));
        var progressId = new ChatMessageId(Guid.Parse("019f0000-0000-7000-8000-000000000013"));
        var answerId = new ChatMessageId(Guid.Parse("019f0000-0000-7000-8000-000000000014"));
        var chat = new ChatThread(_chatId, _projectId, "Chat", _now);
        chat.AddMessage(new ChatMessage(userId, null, ChatMessageRole.User, "Question", _now), _now);
        chat.AddMessage(new ChatMessage(callId, userId, ChatMessageRole.Assistant, "Checking", _now.AddSeconds(1),
            toolCalls: [new ChatToolCall("call-1", "process_run", "{\"command\":\"large\"}")]), _now.AddSeconds(1));
        chat.AddMessage(new ChatMessage(resultId, callId, ChatMessageRole.Tool, "very large result", _now.AddSeconds(2),
            toolCallId: "call-1"), _now.AddSeconds(2));
        chat.AddMessage(new ChatMessage(progressId, resultId, ChatMessageRole.Assistant, "Preparing answer", _now.AddSeconds(3)),
            _now.AddSeconds(3));
        chat.AddMessage(new ChatMessage(answerId, progressId, ChatMessageRole.Assistant, "Final answer", _now.AddSeconds(4)),
            _now.AddSeconds(4));
        _repository.Setup(i => i.GetAsync(_projectId, _chatId, CancellationToken.None))
            .ReturnsAsync(new StoredChat(chat, 7));

        var service = CreateInstance();
        var transcript = await service.GetTranscriptAsync(_projectId.Value, _chatId.Value, CancellationToken.None);

        transcript.ShouldNotBeNull();
        transcript.Messages.Single(message => message.Id == userId.Value).Content.ShouldBe("Question");
        transcript.Messages.Single(message => message.Id == answerId.Value).Content.ShouldBe("Final answer");
        transcript.Messages.Single(message => message.Id == callId.Value).ContentOmitted.ShouldBeTrue();
        transcript.Messages.Single(message => message.Id == callId.Value).ToolCalls!.Single().Arguments.ShouldBeEmpty();
        transcript.Messages.Single(message => message.Id == resultId.Value).ContentOmitted.ShouldBeTrue();
        transcript.Messages.Single(message => message.Id == progressId.Value).ContentOmitted.ShouldBeTrue();

        var activity = await service.GetTurnActivityAsync(
            _projectId.Value, _chatId.Value, userId.Value, answerId.Value, CancellationToken.None);

        activity.ShouldNotBeNull();
        activity.Revision.ShouldBe(7);
        activity.Messages.Count.ShouldBe(3);
        activity.Messages.Single(message => message.Id == callId.Value).Content.ShouldBe("Checking");
        activity.Messages.Single(message => message.Id == callId.Value).ToolCalls!.Single().Arguments
            .ShouldBe("{\"command\":\"large\"}");
        activity.Messages.Single(message => message.Id == resultId.Value).Content.ShouldBeEmpty();
        activity.Messages.Single(message => message.Id == resultId.Value).ContentOmitted.ShouldBeTrue();
        activity.Messages.Single(message => message.Id == progressId.Value).Content.ShouldBe("Preparing answer");

        var content = await service.GetMessageContentAsync(
            _projectId.Value, _chatId.Value, resultId.Value, CancellationToken.None);
        content.ShouldNotBeNull();
        content.Revision.ShouldBe(7);
        content.Content.ShouldBe("very large result");
    }

    [Fact]
    public async Task ShouldAnswerEndpointChangeWithoutToolOutput()
    {
        var userId = new ChatMessageId(Guid.CreateVersion7());
        var resultId = new ChatMessageId(Guid.CreateVersion7());
        var connectionId = Guid.CreateVersion7();
        var chat = new ChatThread(_chatId, _projectId, "Chat", _now);
        chat.AddMessage(new ChatMessage(userId, null, ChatMessageRole.User, "Question", _now), _now);
        chat.AddMessage(new ChatMessage(resultId, userId, ChatMessageRole.Tool, "very large result", _now.AddSeconds(1),
            toolCallId: "call-1"), _now.AddSeconds(1));
        _repository.Setup(i => i.GetAsync(_projectId, _chatId, CancellationToken.None))
            .ReturnsAsync(new StoredChat(chat, 2));
        _clock.SetupGet(i => i.UtcNow).Returns(_now.AddMinutes(1));
        _repository.Setup(i => i.SaveAsync(chat, 2, CancellationToken.None))
            .ReturnsAsync(ChatSaveResult.Saved(3));

        var result = await CreateInstance().UpdateEndpointAsync(
            _projectId.Value, _chatId.Value, new UpdateChatEndpointRequest(connectionId, 2), CancellationToken.None);

        result.ShouldNotBeNull();
        result.ConnectionId.ShouldBe(connectionId);
        result.Revision.ShouldBe(3);
        result.Messages.Single(message => message.Id == userId.Value).Content.ShouldBe("Question");
        var toolResult = result.Messages.Single(message => message.Id == resultId.Value);
        toolResult.Content.ShouldBeEmpty();
        toolResult.ContentOmitted.ShouldBeTrue();
    }

    [Theory]
    [InlineData("{\"isError\":true,\"error\":\"Denied\"}", true)]
    [InlineData("{\"isError\":false,\"content\":[]}", false)]
    [InlineData("legacy result", null)]
    [InlineData("{\"content\":[]}", null)]
    [InlineData("{\"isError\":\"true\"}", null)]
    public async Task ShouldKeepToolErrorStatusInCompactTranscript(string content, bool? expected)
    {
        var userId = new ChatMessageId(Guid.CreateVersion7());
        var resultId = new ChatMessageId(Guid.CreateVersion7());
        var chat = new ChatThread(_chatId, _projectId, "Chat", _now);
        chat.AddMessage(new ChatMessage(userId, null, ChatMessageRole.User, "Question", _now), _now);
        chat.AddMessage(new ChatMessage(resultId, userId, ChatMessageRole.Tool, content, _now.AddSeconds(1),
            toolCallId: "call-1"), _now.AddSeconds(1));
        _repository.Setup(i => i.GetAsync(_projectId, _chatId, CancellationToken.None))
            .ReturnsAsync(new StoredChat(chat, 2));

        var transcript = await CreateInstance().GetTranscriptAsync(_projectId.Value, _chatId.Value, CancellationToken.None);

        var result = transcript!.Messages.Single(message => message.Id == resultId.Value);
        result.Content.ShouldBeEmpty();
        result.ContentOmitted.ShouldBeTrue();
        result.ToolResultIsError.ShouldBe(expected);
    }

    [Fact]
    public async Task ShouldIncludeOnlySmallAppCompactionResultsInExpandedActivity()
    {
        var userId = new ChatMessageId(Guid.NewGuid());
        var callId = new ChatMessageId(Guid.NewGuid());
        var compactResultId = new ChatMessageId(Guid.NewGuid());
        var otherResultId = new ChatMessageId(Guid.NewGuid());
        var answerId = new ChatMessageId(Guid.NewGuid());
        var chat = new ChatThread(_chatId, _projectId, "Chat", _now);
        chat.AddMessage(new ChatMessage(userId, null, ChatMessageRole.User, "Question", _now), _now);
        chat.AddMessage(new ChatMessage(callId, userId, ChatMessageRole.Assistant, string.Empty, _now.AddSeconds(1),
            toolCalls:
            [
                new ChatToolCall("compact", "mcp_app__context_compact", "{\"action\":\"Compact\"}"),
                new ChatToolCall("other", "mcp_built_in__process_run", "{}")
            ]), _now.AddSeconds(1));
        const string compactContent = """
            {"isError":false,"structuredContent":{"action":"Compact","coveredMessages":3,"applied":true}}
            """;
        chat.AddMessage(new ChatMessage(compactResultId, callId, ChatMessageRole.Tool, compactContent,
            _now.AddSeconds(2), toolCallId: "compact"), _now.AddSeconds(2));
        chat.AddMessage(new ChatMessage(otherResultId, compactResultId, ChatMessageRole.Tool, "large ordinary output",
            _now.AddSeconds(3), toolCallId: "other"), _now.AddSeconds(3));
        chat.AddMessage(new ChatMessage(answerId, otherResultId, ChatMessageRole.Assistant, "Done",
            _now.AddSeconds(4)), _now.AddSeconds(4));
        _repository.Setup(i => i.GetAsync(_projectId, _chatId, CancellationToken.None))
            .ReturnsAsync(new StoredChat(chat, 1));

        var service = CreateInstance();
        var transcript = await service.GetTranscriptAsync(_projectId.Value, _chatId.Value, CancellationToken.None);
        transcript!.Messages.Single(message => message.Id == compactResultId.Value).ContentOmitted.ShouldBeTrue();

        var activity = await service.GetTurnActivityAsync(
            _projectId.Value, _chatId.Value, userId.Value, answerId.Value, CancellationToken.None);
        activity!.Messages.Single(message => message.Id == compactResultId.Value).Content.ShouldBe(compactContent);
        activity.Messages.Single(message => message.Id == compactResultId.Value).ContentOmitted.ShouldBeFalse();
        activity.Messages.Single(message => message.Id == otherResultId.Value).ContentOmitted.ShouldBeTrue();
    }

    [Fact]
    public async Task ShouldListPinnedChatsInManualOrderAndTheRestByActivity()
    {
        StoredChatSummary Summary(string title, int activityMinute, bool pinned = false, string? order = null, int pinnedMinute = 0) =>
            new(new ChatId(Guid.CreateVersion7()), _projectId, title, _now, 1, _now.AddMinutes(activityMinute),
                pinned, pinned ? _now.AddMinutes(pinnedMinute) : null, PinOrder: order);
        _repository.Setup(i => i.ListSummariesAsync(_projectId, CancellationToken.None)).ReturnsAsync([
            Summary("old", 1),
            Summary("pinned-k", 9, pinned: true, order: "k"),
            Summary("recent", 5),
            Summary("legacy-late", 8, pinned: true, pinnedMinute: 2),
            Summary("pinned-V", 3, pinned: true, order: "V"),
            Summary("legacy-early", 1, pinned: true, pinnedMinute: 1)
        ]);

        var chats = await CreateInstance().ListAsync(_projectId.Value, CancellationToken.None);

        chats.Select(chat => chat.Title).ShouldBe(["legacy-early", "legacy-late", "pinned-V", "pinned-k", "recent", "old"]);
    }

    [Fact]
    public async Task ShouldPlacePinnedChatBeforeTheRequestedChat()
    {
        var first = new ChatId(Guid.CreateVersion7());
        var second = new ChatId(Guid.CreateVersion7());
        var chat = new ChatThread(_chatId, _projectId, "Chat", _now);
        _repository.Setup(i => i.ListSummariesAsync(_projectId, CancellationToken.None)).ReturnsAsync([
            new StoredChatSummary(first, _projectId, "First", _now, 1, _now, true, _now, PinOrder: "V"),
            new StoredChatSummary(second, _projectId, "Second", _now, 1, _now, true, _now, PinOrder: "k"),
            new StoredChatSummary(_chatId, _projectId, "Chat", _now, 2, _now, false, null)
        ]);
        _repository.Setup(i => i.GetAsync(_projectId, _chatId, CancellationToken.None)).ReturnsAsync(new StoredChat(chat, 2));
        _clock.SetupGet(i => i.UtcNow).Returns(_now.AddMinutes(1));
        _repository.Setup(i => i.SaveAsync(chat, 2, CancellationToken.None)).ReturnsAsync(ChatSaveResult.Saved(3));

        var result = await CreateInstance().PinAsync(_projectId.Value, _chatId.Value,
            new PinChatRequest(true, 2, second.Value), CancellationToken.None);

        result.ShouldNotBeNull().IsPinned.ShouldBeTrue();
        string.CompareOrdinal("V", chat.PinOrder).ShouldBeLessThan(0);
        string.CompareOrdinal(chat.PinOrder, "k").ShouldBeLessThan(0);
    }

    [Fact]
    public async Task ShouldKeyLegacyPinnedChatsBeforePlacingInFrontOfOne()
    {
        var legacyId = new ChatId(Guid.CreateVersion7());
        var legacy = new ChatThread(legacyId, _projectId, "Legacy", _now);
        legacy.RestorePinState(true, _now);
        var chat = new ChatThread(_chatId, _projectId, "Chat", _now);
        var legacySummary = new StoredChatSummary(legacyId, _projectId, "Legacy", _now, 4, _now, true, _now);
        _repository.SetupSequence(i => i.ListSummariesAsync(_projectId, CancellationToken.None))
            .ReturnsAsync([legacySummary])
            .ReturnsAsync([legacySummary with { PinOrder = "V", Revision = 5 }]);
        _repository.Setup(i => i.GetAsync(_projectId, legacyId, CancellationToken.None)).ReturnsAsync(new StoredChat(legacy, 4));
        _repository.Setup(i => i.SaveAsync(legacy, 4, CancellationToken.None)).ReturnsAsync(ChatSaveResult.Saved(5));
        _repository.Setup(i => i.GetAsync(_projectId, _chatId, CancellationToken.None)).ReturnsAsync(new StoredChat(chat, 1));
        _repository.Setup(i => i.SaveAsync(chat, 1, CancellationToken.None)).ReturnsAsync(ChatSaveResult.Saved(2));
        _clock.SetupGet(i => i.UtcNow).Returns(_now.AddMinutes(1));

        await CreateInstance().PinAsync(_projectId.Value, _chatId.Value,
            new PinChatRequest(true, 1, legacyId.Value), CancellationToken.None);

        legacy.PinOrder.ShouldBe("V");
        string.CompareOrdinal(chat.PinOrder, "V").ShouldBeLessThan(0);
    }

    private ChatService CreateInstance() => new(_repository.Object, _idGenerator.Object, _clock.Object,
        new ChatSynchronization(), new PinOrderKeys(), new ChatKindPolicyRegistry([new ConversationChatKindPolicy()]));
}
