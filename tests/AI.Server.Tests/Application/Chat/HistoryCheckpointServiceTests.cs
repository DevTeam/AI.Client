namespace AI.Application.Tests.Chat;

using AI.Application.Chat;
using AI.Contracts.Chat;
using AI.Contracts.Chats;
using Shouldly;
using Xunit;

public sealed class HistoryCheckpointServiceTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ChatId = Guid.NewGuid();
    private readonly HistoryCheckpointService _service = new(new InMemoryHistoryCheckpoints(), new ContextTokenEstimator());

    [Fact]
    public void ShouldReplaceHistoryUpToTheQuestionAfterTheCheckpointAndKeepInstructions()
    {
        var answer = Guid.NewGuid();
        ChatCompletionMessage[] context =
        [
            new("system", "Be helpful"),
            new("user", "First", MessageId: Guid.NewGuid()),
            new("assistant", "", [new ChatToolCall("call-1", "read", "{}")], MessageId: answer),
            // Repaired after the covered message; it must go with it, not dangle without its call.
            new("tool", "interrupted", ToolCallId: "call-1"),
            new("user", "Second", MessageId: Guid.NewGuid())
        ];

        var applied = _service.Apply(context, Checkpoint(answer, "Summary"));

        applied.Select(message => message.Role).ShouldBe(["system", "user", "user"]);
        applied[1].Content.ShouldEndWith("Summary");
        applied[1].MessageId.ShouldBe(answer);
        applied[2].Content.ShouldBe("Second");
    }

    [Fact]
    public void ShouldLeaveAContextWithNoQuestionAfterTheCheckpointAlone()
    {
        var answer = Guid.NewGuid();
        ChatCompletionMessage[] context = [new("user", "Only", MessageId: Guid.NewGuid()), new("assistant", "Done", MessageId: answer)];

        _service.Apply(context, Checkpoint(answer, "Summary")).ShouldBeSameAs(context);
    }

    [Fact]
    public async Task ShouldApplyTheDeepestCheckpointOnTheBranchAndIgnoreOnesFromOtherBranches()
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        ChatCompletionMessage[] context =
        [
            new("user", "q1", MessageId: ids[0]), new("assistant", "a1", MessageId: ids[1]),
            new("user", "q2", MessageId: ids[2]), new("assistant", "a2", MessageId: ids[3]),
            new("user", "q3", MessageId: ids[4])
        ];
        await _service.AddAsync(ProjectId, ChatId, Checkpoint(ids[3], "deep"), CancellationToken.None);
        await _service.AddAsync(ProjectId, ChatId, Checkpoint(ids[1], "shallow"), CancellationToken.None);
        await _service.AddAsync(ProjectId, ChatId, Checkpoint(Guid.NewGuid(), "another branch"), CancellationToken.None);

        var applied = await _service.ApplyAsync(ProjectId, ChatId, context, CancellationToken.None);

        applied.Select(message => message.Content).ShouldBe([HistoryCheckpointService.SummaryPrefix + "deep", "q3"]);
    }

    [Fact]
    public void ShouldNotOfferToSummarizeALoneEarlierSummaryAgain()
    {
        var answer = Guid.NewGuid();
        var summarized = _service.Apply(
        [
            new("user", "q1", MessageId: Guid.NewGuid()), new("assistant", "a1", MessageId: answer),
            new("user", "q2", MessageId: Guid.NewGuid()), new("assistant", "a2", MessageId: Guid.NewGuid()),
            new("user", "q3", MessageId: Guid.NewGuid())
        ], Checkpoint(answer, "Summary"));

        _service.Coverable(summarized, new HistoryKeepPolicy(100_000, 2, 2)).ShouldBeEmpty();
        _service.Coverable(summarized, new HistoryKeepPolicy(100_000, 1, 1)).Select(message => message.Content).ShouldBe([summarized[0].Content, "q2", "a2"]);
    }

    [Fact]
    public void ShouldKeepRecentTurnsByTheirSizeNotByTheirNumber()
    {
        var huge = new string('x', 40_000);
        ChatCompletionMessage[] context =
        [
            new("user", "q1", MessageId: Guid.NewGuid()), new("tool", huge, ToolCallId: "a", MessageId: Guid.NewGuid()),
            new("user", "q2", MessageId: Guid.NewGuid()), new("tool", huge, ToolCallId: "b", MessageId: Guid.NewGuid()),
            new("user", "q3", MessageId: Guid.NewGuid()), new("assistant", "short", MessageId: Guid.NewGuid())
        ];

        // Two huge turns and a small one: only the small one fits the budget, so both huge ones go.
        _service.Coverable(context, new HistoryKeepPolicy(5_000, 0, 2)).Count.ShouldBe(4);
        // Between turns nothing has to stay: a huge last turn is summarized with the rest.
        _service.Coverable(context[..4], new HistoryKeepPolicy(5_000, 0, 2)).Count.ShouldBe(4);
        // During a run the turn in progress stays, however large.
        _service.Coverable(context[..4], new HistoryKeepPolicy(5_000, 1, 2)).Count.ShouldBe(2);
        // Room for everything still leaves no more than the most recent turns.
        _service.Coverable(context, new HistoryKeepPolicy(1_000_000, 0, 2)).Count.ShouldBe(2);
    }

    [Fact]
    public void ShouldApplyACheckpointCoveringTheWholeBranchOnceTheNextQuestionArrives()
    {
        var head = Guid.NewGuid();
        ChatCompletionMessage[] before = [new("user", "q1", MessageId: Guid.NewGuid()), new("assistant", "a1", MessageId: head)];

        var applied = _service.Apply([.. before, new("user", "q2")], Checkpoint(head, "Summary"));

        applied.Select(message => message.Content).ShouldBe([HistoryCheckpointService.SummaryPrefix + "Summary", "q2"]);
    }

    [Fact]
    public async Task ShouldDeleteACheckpoint()
    {
        var checkpoint = await _service.AddAsync(ProjectId, ChatId, Checkpoint(Guid.NewGuid(), "x"), CancellationToken.None);

        (await _service.DeleteAsync(ProjectId, ChatId, checkpoint.Id, CancellationToken.None)).ShouldBeTrue();
        (await _service.DeleteAsync(ProjectId, ChatId, checkpoint.Id, CancellationToken.None)).ShouldBeFalse();
        (await _service.ListAsync(ProjectId, ChatId, CancellationToken.None)).ShouldBeEmpty();
    }

    private static HistoryCheckpoint Checkpoint(Guid upTo, string summary) =>
        new(Guid.CreateVersion7(), upTo, summary, 2, 100, "m", DateTimeOffset.UtcNow, HistoryCheckpointOrigin.Manual);
}
