namespace AI.Application.Tests.Chat;

using AI.Application.Chat;
using AI.Application.Projects;
using AI.Application.Tools;
using AI.Contracts.Chat;
using AI.Contracts.Chats;
using Moq;
using Shouldly;
using Xunit;

public sealed class ModelContentCheckpointServiceTests
{
    private readonly InMemoryHistoryCheckpoints _store = new();
    private readonly HistoryCheckpointService _history;
    private readonly ModelContentCheckpointService _service;
    private readonly ToolRunContext _run = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true);

    public ModelContentCheckpointServiceTests()
    {
        _history = new HistoryCheckpointService(_store);
        var clock = new Mock<IClock>();
        clock.SetupGet(item => item.UtcNow).Returns(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
        var ids = new Mock<IIdGenerator>();
        ids.Setup(item => item.Create()).Returns(Guid.CreateVersion7);
        _service = new ModelContentCheckpointService(new ContextSummaryWriter(), _history, clock.Object, ids.Object);
    }

    [Fact]
    public async Task ShouldReplaceCompletedCurrentTurnWorkOnlyInModelProjectionAndResetIt()
    {
        var largeResult = new string('x', 10_000);
        ChatCompletionMessage[] beforeCall =
        [
            new("user", "Investigate"),
            new("assistant", "", [new ChatToolCall("read-1", "file_read", "{}")]),
            new("tool", largeResult, ToolCallId: "read-1"),
            new("assistant", "", [new ChatToolCall("compact-1", "app_context_compact", "{}")])
        ];
        using var scope = _service.Begin(_run, "m", (_, _) => Task.FromResult("Read the file and found the cause."));
        _service.Update(_run, beforeCall);

        var result = await _service.CompactAsync(_run, 500, ContextCompactionScope.Turn, CancellationToken.None);
        var complete = beforeCall.Append(new ChatCompletionMessage("tool", "compacted", ToolCallId: "compact-1")).ToArray();
        var projected = _service.Apply(_run, complete);

        result.Applied.ShouldBeTrue();
        result.CoveredMessages.ShouldBe(2);
        projected.Count.ShouldBe(4);
        projected[0].Content.ShouldBe("Investigate");
        projected[1].Content.ShouldContain("found the cause");
        projected[2].ToolCalls!.Single().Id.ShouldBe("compact-1");
        complete[2].Content.ShouldBeSameAs(largeResult);

        (await _service.ResetAsync(_run, ContextCompactionScope.Turn, CancellationToken.None)).ShouldBeTrue();
        _service.Apply(_run, complete).ShouldBeSameAs(complete);
    }

    [Fact]
    public async Task ShouldSummarizeEveryPartOfALongTurnInsteadOfItsFirstCharacters()
    {
        var prompts = new List<string>();
        ChatCompletionMessage[] context =
        [
            new("user", "Investigate"),
            .. Enumerable.Range(0, 12).SelectMany(index => new ChatCompletionMessage[]
            {
                new("assistant", $"step {index}", [new ChatToolCall($"read-{index}", "file_read", "{}")]),
                new("tool", $"marker-{index} " + new string('x', 3_900), ToolCallId: $"read-{index}")
            }),
            new("assistant", "", [new ChatToolCall("compact-1", "app_context_compact", "{}")])
        ];
        using var scope = _service.Begin(_run, "m", (prompt, _) =>
        {
            prompts.Add(prompt);
            return Task.FromResult("summary");
        });
        _service.Update(_run, context);

        await _service.CompactAsync(_run, 500, ContextCompactionScope.Turn, CancellationToken.None);

        // 47k characters of work: summarized in parts, then merged — and the last result is in them.
        prompts.Count.ShouldBeGreaterThan(1);
        prompts.ShouldContain(prompt => prompt.Contains("marker-11", StringComparison.Ordinal));
        prompts[^1].ShouldStartWith("Merge these summaries");
    }

    [Fact]
    public async Task ShouldKeepAHistorySummaryForLaterRequestsAndRestoreTheHistoryOnReset()
    {
        var (first, firstAnswer, second, secondAnswer) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        ChatCompletionMessage[] context =
        [
            new("user", "First question", MessageId: first),
            new("assistant", "First answer", MessageId: firstAnswer),
            new("user", "Second question", MessageId: second),
            new("assistant", "Second answer", MessageId: secondAnswer),
            new("user", "Third question", MessageId: Guid.NewGuid()),
            new("assistant", "", [new ChatToolCall("compact-1", "app_context_compact", "{}")])
        ];
        using var scope = _service.Begin(_run, "model-x", (_, _) => Task.FromResult("They talked about the first question."));
        _service.Update(_run, context);

        _service.Preview(_run, ContextCompactionScope.History).CoveredMessages.ShouldBe(2);
        var result = await _service.CompactAsync(_run, 500, ContextCompactionScope.History, CancellationToken.None);
        var projected = _service.Apply(_run, context);
        var kept = await _history.ListAsync(_run.ProjectId, _run.ChatId, CancellationToken.None);

        result.Applied.ShouldBeTrue();
        kept.Single().UpToMessageId.ShouldBe(firstAnswer);
        kept.Single().Model.ShouldBe("model-x");
        kept.Single().Origin.ShouldBe(HistoryCheckpointOrigin.Model);
        projected.Select(message => message.Content).ShouldBe([
            HistoryCheckpointService.SummaryPrefix + "They talked about the first question.",
            "Second question", "Second answer", "Third question", ""]);
        // A later turn built from the stored branch starts from the same summary.
        (await _history.ApplyAsync(_run.ProjectId, _run.ChatId, context, CancellationToken.None))[0].Content
            .ShouldBe(projected[0].Content);

        (await _service.ResetAsync(_run, ContextCompactionScope.History, CancellationToken.None)).ShouldBeTrue();
        _service.Apply(_run, context).ShouldBeSameAs(context);
        (await _history.ListAsync(_run.ProjectId, _run.ChatId, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ShouldRefuseToCompactHistoryWhenOnlyTheRecentTurnsAreLeft()
    {
        ChatCompletionMessage[] context =
        [
            new("user", "Question", MessageId: Guid.NewGuid()),
            new("assistant", "Answer", MessageId: Guid.NewGuid()),
            new("user", "Follow-up", MessageId: Guid.NewGuid())
        ];
        using var scope = _service.Begin(_run, "m", (_, _) => Task.FromResult("never"));
        _service.Update(_run, context);

        var result = await _service.CompactAsync(_run, 500, ContextCompactionScope.History, CancellationToken.None);

        result.Applied.ShouldBeFalse();
        (await _history.ListAsync(_run.ProjectId, _run.ChatId, CancellationToken.None)).ShouldBeEmpty();
    }
}

internal sealed class InMemoryHistoryCheckpoints : IHistoryCheckpointRepository
{
    private readonly Dictionary<(Guid, Guid), IReadOnlyList<HistoryCheckpoint>> _chats = [];

    public Task<IReadOnlyList<HistoryCheckpoint>> ListAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken) =>
        Task.FromResult(_chats.GetValueOrDefault((projectId, chatId)) ?? []);

    public Task<IReadOnlyList<HistoryCheckpoint>> UpdateAsync(Guid projectId, Guid chatId,
        Func<IReadOnlyList<HistoryCheckpoint>, IReadOnlyList<HistoryCheckpoint>> change, CancellationToken cancellationToken)
    {
        var next = change(_chats.GetValueOrDefault((projectId, chatId)) ?? []);
        _chats[(projectId, chatId)] = next;
        return Task.FromResult(next);
    }
}
