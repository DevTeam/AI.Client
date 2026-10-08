namespace AI.Application.Tests.Chat;

using AI.Application.Chat;
using AI.Application.Projects;
using AI.Application.Tools;
using AI.Contracts.Chat;
using AI.Contracts.Chats;
using AI.Contracts.Settings;
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
        _history = new HistoryCheckpointService(_store, new ContextTokenEstimator());
        var clock = new Mock<IClock>();
        clock.SetupGet(item => item.UtcNow).Returns(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
        var ids = new Mock<IIdGenerator>();
        ids.Setup(item => item.Create()).Returns(Guid.CreateVersion7);
        _service = new ModelContentCheckpointService(new ContextSummaryWriter(new ContextTokenEstimator(), new ToolResultContextProjector(), new AdaptiveContextPolicy(new ContextTokenEstimator(), new AI.Contracts.Settings.ConnectionContextLimitsResolver())), _history, clock.Object, ids.Object, new ContextTokenEstimator(), new AdaptiveContextPolicy(new ContextTokenEstimator(), new AI.Contracts.Settings.ConnectionContextLimitsResolver()));
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
        using var scope = _service.Begin(_run, "m", 100_000, (_, _) => Task.FromResult("Read the file and found the cause."));
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
    public async Task ShouldSummarizeTheCompletedStepsOfALongTurnAndKeepTheRecentOnes()
    {
        var prompts = new List<string>();
        var context = new List<ChatCompletionMessage> { new("user", "Investigate") };
        void Step(int index) => context.AddRange(
        [
            new ChatCompletionMessage("assistant", $"step {index}", [new ChatToolCall($"read-{index}", "file_read", "{}")]),
            new ChatCompletionMessage("tool", $"marker-{index} " + new string('x', 4_000), ToolCallId: $"read-{index}")
        ]);
        for (var index = 0; index < 6; index++) Step(index);
        using var scope = _service.Begin(_run, "m", 100_000, (prompt, _) =>
        {
            prompts.Add(prompt);
            return Task.FromResult($"summary {prompts.Count}");
        });
        _service.Update(_run, context);

        // Room for two steps of about 2k tokens each: the four before them are summarized.
        var first = await _service.CompactTurnAheadAsync(_run, 4_500, 1_000, 500, CancellationToken.None);
        var projected = _service.Apply(_run, context);

        first.Applied.ShouldBeTrue();
        first.CoveredMessages.ShouldBe(8);
        projected.Select(message => message.Content).ShouldBe(
            ["Investigate", "Compacted completed work from this turn:\nsummary 1", "step 4", context[10].Content, "step 5", context[12].Content]);

        // Later steps are summarized with the earlier summary folded in, never the steps it replaced.
        for (var index = 6; index < 9; index++) Step(index);
        _service.Update(_run, context);
        var second = await _service.CompactTurnAheadAsync(_run, 4_500, 1_000, 500, CancellationToken.None);

        second.Applied.ShouldBeTrue();
        prompts[^1].ShouldContain("summary 1");
        prompts[^1].ShouldNotContain("marker-0");
        prompts[^1].ShouldContain("marker-4");
        _service.Apply(_run, context)[1].Content.ShouldEndWith("summary 2");
    }

    [Fact]
    public async Task ShouldKeepTheLoadedSkillInstructionsWhileCompactingLaterSteps()
    {
        var instructions = "Follow this skill playbook: " + new string('s', 2_000);
        var context = new List<ChatCompletionMessage>
        {
            new("user", "Configure the project"),
            new("assistant", "", [new ChatToolCall("skill-1", "skills__run_skill", "{}")]),
            new("tool", instructions, ToolCallId: "skill-1")
        };
        var summaries = 0;
        using var scope = _service.Begin(_run, "m", 100_000, (_, _) =>
        {
            summaries++;
            return Task.FromResult("Inspected the project files.");
        });
        _service.Update(_run, context);

        (await _service.CompactTurnAheadAsync(_run, 1, 100, 500, CancellationToken.None)).Applied.ShouldBeFalse();
        summaries.ShouldBe(0);

        for (var index = 0; index < 4; index++)
        {
            context.Add(new("assistant", "", [new ChatToolCall($"read-{index}", "file_read", "{}")]));
            context.Add(new("tool", new string('x', 2_000), ToolCallId: $"read-{index}"));
        }
        _service.Update(_run, context);
        var result = await _service.CompactTurnAheadAsync(_run, 1, 100, 500, CancellationToken.None);
        var projected = _service.Apply(_run, context);

        result.Applied.ShouldBeTrue();
        summaries.ShouldBe(1);
        projected[0].ShouldBe(context[0]);
        projected[1].ShouldBe(context[1]);
        projected[2].ShouldBe(context[2]);
        projected[3].Content.ShouldContain("Inspected the project files.");
        projected.Count.ShouldBeLessThan(context.Count);
    }

    [Fact]
    public async Task ShouldNotSummarizeTurnStepsTooSmallToBeWorthIt()
    {
        ChatCompletionMessage[] context =
        [
            new("user", "Investigate"),
            new("assistant", "", [new ChatToolCall("read-1", "file_read", "{}")]),
            new("tool", "short", ToolCallId: "read-1"),
            new("assistant", "", [new ChatToolCall("read-2", "file_read", "{}")]),
            new("tool", "short", ToolCallId: "read-2")
        ];
        using var scope = _service.Begin(_run, "m", 100_000, (_, _) => Task.FromResult("summary"));
        _service.Update(_run, context);

        (await _service.CompactTurnAheadAsync(_run, 10, 1_000, 500, CancellationToken.None)).Applied.ShouldBeFalse();
        _service.Apply(_run, context).ShouldBeSameAs(context);
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
        using var scope = _service.Begin(_run, "m", 100_000, (prompt, _) =>
        {
            prompts.Add(prompt);
            return Task.FromResult("summary");
        }, new ConnectionSettings(Guid.NewGuid(), "Test", "https://example.test/v1", "m", true, true, false,
            ContextWindowTokens: 8_192, ReservedOutputTokens: 1_000));
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
        using var scope = _service.Begin(_run, "model-x", 100_000, (_, _) => Task.FromResult("They talked about the first question."));
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
        using var scope = _service.Begin(_run, "m", 100_000, (_, _) => Task.FromResult("never"));
        _service.Update(_run, context);

        var result = await _service.CompactAsync(_run, 500, ContextCompactionScope.History, CancellationToken.None);

        result.Applied.ShouldBeFalse();
        (await _history.ListAsync(_run.ProjectId, _run.ChatId, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ShouldRejectInflatedAutomaticHistorySummaryAndKeepThePreviousCheckpoint()
    {
        var context = new List<ChatCompletionMessage>
        {
            new("user", new string('x', 4_000), MessageId: Guid.NewGuid()),
            new("assistant", new string('y', 4_000), MessageId: Guid.NewGuid()),
            new("user", new string('a', 100), MessageId: Guid.NewGuid()),
            new("assistant", new string('b', 500), MessageId: Guid.NewGuid()),
            new("user", "Current question", MessageId: Guid.NewGuid())
        };
        var calls = 0;
        using var scope = _service.Begin(_run, "m", 100_000, (_, _) =>
            Task.FromResult(++calls == 1 ? "First checkpoint." : new string('я', 1_000)));
        _service.Update(_run, context);
        var first = await _service.CompactAsync(_run, 500, ContextCompactionScope.History, CancellationToken.None,
            HistoryCheckpointOrigin.Automatic, 200);
        first.Applied.ShouldBeTrue();
        var checkpoint = (await _history.ListAsync(_run.ProjectId, _run.ChatId, CancellationToken.None)).Single();
        context.Add(new("assistant", "Short answer", MessageId: Guid.NewGuid()));
        context.Add(new("user", "Next question", MessageId: Guid.NewGuid()));
        _service.Update(_run, context);
        var before = _service.Apply(_run, context);
        var second = await _service.CompactAsync(_run, 500, ContextCompactionScope.History, CancellationToken.None,
            HistoryCheckpointOrigin.Automatic, 200);
        second.Applied.ShouldBeFalse();
        calls.ShouldBe(2);
        _service.Apply(_run, context).ShouldBe(before);
        (await _history.ListAsync(_run.ProjectId, _run.ChatId, CancellationToken.None)).ShouldBe([checkpoint]);
    }

    [Fact]
    public async Task ShouldRejectAnInflatedTurnSummaryWithoutReplacingTheAcceptedOne()
    {
        var context = new List<ChatCompletionMessage> { new("user", "Question") };
        void Step(int index) => context.AddRange([
            new ChatCompletionMessage("assistant", "", [new($"step-{index}", "read", "{}")]),
            new ChatCompletionMessage("tool", new string('x', 300), ToolCallId: $"step-{index}")]);
        for (var index = 0; index < 8; index++) Step(index);
        var calls = 0;
        using var scope = _service.Begin(_run, "m", 100_000, (_, _) =>
            Task.FromResult(++calls == 1 ? "Accepted summary" : new string('я', 1_000)));
        _service.Update(_run, context);
        (await _service.CompactTurnAheadAsync(_run, 1, 100, 500, CancellationToken.None)).Applied.ShouldBeTrue();
        Step(8);
        _service.Update(_run, context);
        var before = _service.Apply(_run, context);
        (await _service.CompactTurnAheadAsync(_run, 1, 100, 500, CancellationToken.None)).Applied.ShouldBeFalse();
        calls.ShouldBe(2);
        _service.Apply(_run, context).ShouldBe(before);
    }

    [Fact]
    public void ShouldEstimatePreviewTokensForMultilingualHistoryInsteadOfHalvingCharacterCount()
    {
        ChatCompletionMessage[] context = [
            new("user", new string('я', 1_000), MessageId: Guid.NewGuid()),
            new("assistant", "中文", MessageId: Guid.NewGuid()),
            new("user", "Recent", MessageId: Guid.NewGuid()),
            new("assistant", "Answer", MessageId: Guid.NewGuid()),
            new("user", "Current", MessageId: Guid.NewGuid())];
        using var scope = _service.Begin(_run, "m", 100_000, (_, _) => Task.FromResult("Unused"));
        _service.Update(_run, context);
        var preview = _service.Preview(_run, ContextCompactionScope.History);
        preview.SourceTokens.ShouldBe(new ContextTokenEstimator().EstimateMessages(context.Take(2).ToArray()));
        preview.SourceTokens.ShouldBeGreaterThan(preview.SourceCharacters / 2);
    }

    [Theory]
    [InlineData(8_192)]
    [InlineData(32_768)]
    public async Task ShouldThrottleAutomaticSummariesAcrossALongTurnAndPreserveAppendOnlyPrefixes(long window)
    {
        var estimator = new ContextTokenEstimator();
        var policy = new AdaptiveContextPolicy(estimator, new ConnectionContextLimitsResolver());
        var connection = new ConnectionSettings(Guid.NewGuid(), "Test", "https://example.test/v1", "m", true, true, false,
            ContextWindowTokens: window, ReservedOutputTokens: 1_000);
        var budget = policy.ResolveCompaction(connection, 300, 100);
        var context = new List<ChatCompletionMessage> { new("user", "Keep the current request") };
        var calls = 0;
        var accepted = 0;
        long nextAttempt = 0;
        using var scope = _service.Begin(_run, "m", budget.HistoryKeepTokens, (_, _) =>
        {
            calls++;
            return Task.FromResult("Completed the earlier inspections. Continue the current task.");
        });
        for (var step = 0; step < (window == 8_192 ? 80 : 160); step++)
        {
            context.Add(new("assistant", "", [new($"call-{step}", "read", "{}") ]));
            context.Add(new("tool", new string('x', 500), ToolCallId: $"call-{step}"));
            _service.Update(_run, context);
            _service.UpdateBudget(_run, budget);
            var projected = _service.Apply(_run, context);
            var size = estimator.EstimateMessages(projected);
            if (!policy.ShouldCompactAhead(budget, size, nextAttempt)) continue;
            var result = await _service.CompactTurnAheadAsync(_run, budget.HistoryKeepTokens, budget.MinimumGainTokens,
                budget.SummaryTargetTokens, CancellationToken.None);
            var after = _service.Apply(_run, context);
            if (result.Applied)
            {
                accepted++;
                policy.ShouldAcceptCompaction(projected, after, budget.MinimumGainTokens).ShouldBeTrue();
            }
            nextAttempt = estimator.EstimateMessages(after) + budget.RetryGrowthTokens;
            policy.ShouldCompactAhead(budget, estimator.EstimateMessages(after), nextAttempt).ShouldBeFalse();
            after[0].ShouldBe(context[0]);
            after.TakeLast(2).ShouldBe(context.TakeLast(2));
        }
        accepted.ShouldBeGreaterThan(0);
        calls.ShouldBe(accepted);
        calls.ShouldBeLessThan(12);
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
