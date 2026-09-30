namespace AI.Application.Tests.Chat;

using System.Text;
using System.Text.Json;
using System.Threading;
using AI.Application.Chat;
using AI.Contracts.Chat;
using AI.Contracts.Settings;
using Shouldly;
using Xunit;

public sealed class ChatContextPlannerTests
{
    private readonly ContextTokenEstimator _estimator = new();

    [Fact]
    public void ShouldPassSmallContextWithoutChangingMessages()
    {
        ChatCompletionMessage[] messages = [new("user", "Hello")];
        var planner = Planner();

        var plan = planner.Plan(null, "unknown-model", messages, []);

        plan.Fits.ShouldBeTrue();
        plan.Messages.ShouldBeSameAs(messages);
        plan.WasCompacted.ShouldBeFalse();
        plan.OmittedMessages.ShouldBe(0);
    }

    [Fact]
    public void ShouldSplitInstructionTokensFromConversation()
    {
        ChatCompletionMessage[] system = [new("system", new string('s', 400))];
        ChatCompletionMessage[] messages = [.. system, new("user", "Hello"), new("assistant", "Hi")];

        var plan = Planner().Plan(null, "unknown-model", messages, []);

        plan.InstructionTokens.ShouldBe(_estimator.EstimateMessages(system));
        plan.EstimatedInputTokens.ShouldBeGreaterThan(plan.InstructionTokens);
        plan.OverheadTokens.ShouldBe(256 + 1_024);
    }

    [Fact]
    public void ShouldSubtractToolDefinitionsFromInputLimit()
    {
        var planner = Planner();
        var withoutTools = planner.Plan(null, "unknown-model", [], []);
        ChatToolDefinition[] tools =
        [
            new("read_file", new string('d', 2_000),
                JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"}}}""").RootElement.Clone())
        ];

        var withTools = planner.Plan(null, "unknown-model", [], tools);

        withTools.ToolDefinitionTokens.ShouldBeGreaterThan(0);
        withTools.InputLimit.ShouldBe(withoutTools.InputLimit - withTools.ToolDefinitionTokens);
    }

    [Fact]
    public void ShouldUseConservativeUtf8EstimateForNonAsciiText()
    {
        const string text = "Путь не найден";

        var estimate = _estimator.EstimateMessages([new ChatCompletionMessage("user", text)]);

        estimate.ShouldBeGreaterThanOrEqualTo((Encoding.UTF8.GetByteCount(text) + 1L) / 2L);
    }

    [Fact]
    public void ShouldNotOverflowWhenAnEstimateSaturates()
    {
        var saturated = new SaturatedEstimator();
        var planner = new ChatContextPlanner(saturated, new ChatContextCompactor(saturated, new ContextSummaryWriter()),
            new ConnectionContextLimitsResolver());

        var plan = planner.Plan(null, "unknown-model", [new ChatCompletionMessage("user", "large")],
            [new ChatToolDefinition("tool", "large", JsonDocument.Parse("{}").RootElement.Clone())]);

        plan.EstimatedInputTokens.ShouldBe(long.MaxValue);
        plan.ToolDefinitionTokens.ShouldBe(long.MaxValue);
        plan.InputLimit.ShouldBe(0);
        plan.Fits.ShouldBeFalse();
    }

    [Fact]
    public void ShouldGiveUnknownModelsConservativeDefaults()
    {
        var plan = Planner().Plan(null, "vendor-specific-model", [], []);

        plan.InputLimit.ShouldBe(27_392);
        plan.ReservedOutputTokens.ShouldBe(4_096);
        plan.ContextWindowSource.ShouldBe(ContextLimitSource.Default);
        plan.ReservedOutputSource.ShouldBe(ContextLimitSource.Default);
    }

    [Fact]
    public void ShouldUseConnectionLimitOverridesAndExposeTheirSource()
    {
        var connection = new ConnectionSettings(Guid.NewGuid(), "Large", "https://example.test/v1", "model",
            true, true, false, ContextWindowTokens: 65_536, ReservedOutputTokens: 8_192);

        var plan = Planner().Plan(connection, connection.Model, [], []);

        plan.ContextWindowTokens.ShouldBe(65_536);
        plan.ReservedOutputTokens.ShouldBe(8_192);
        plan.InputLimit.ShouldBe(56_064);
        plan.ContextWindowSource.ShouldBe(ContextLimitSource.Override);
        plan.ReservedOutputSource.ShouldBe(ContextLimitSource.Override);
    }

    [Fact]
    public void ShouldCompactLargeToolProjectionsWithoutChangingStoredContentOrCallOrder()
    {
        var firstStored = new string('a', 30_000);
        var secondStored = new string('b', 30_000);
        ChatCompletionMessage[] messages =
        [
            new("user", "Inspect both"),
            new("assistant", "", [new ChatToolCall("call-1", "first", "{}"), new ChatToolCall("call-2", "second", "{}")]),
            new("tool", firstStored, ToolCallId: "call-1"),
            new("tool", secondStored, ToolCallId: "call-2")
        ];

        var plan = Planner().Plan(null, "unknown-model", messages, []);

        plan.Fits.ShouldBeTrue();
        plan.WasCompacted.ShouldBeTrue();
        plan.OmittedMessages.ShouldBe(0);
        plan.Messages.Where(message => message.Role == "tool").Select(message => message.ToolCallId)
            .ShouldBe(["call-1", "call-2"]);
        plan.Messages[2].Content.ShouldBeSameAs(firstStored);
        plan.Messages[3].Content.ShouldBeSameAs(secondStored);
        plan.Messages[2].ModelContent.ShouldNotBeNull().ShouldContain("Tool: first");
        plan.Messages[3].ModelContent.ShouldNotBeNull().ShouldContain("Tool: second");
        messages[2].ModelContent.ShouldBeNull();
        messages[3].ModelContent.ShouldBeNull();
    }

    [Fact]
    public void ShouldTightenCurrentTurnToolProjectionsUntilTheyFit()
    {
        var stored = new string('x', 50_000);
        ChatCompletionMessage[] messages =
        [
            new("user", "Inspect the result"),
            new("assistant", "", [new ChatToolCall("call-1", "read", "{}")]),
            new("tool", stored, ToolCallId: "call-1")
        ];
        var planner = new ChatContextPlanner(_estimator, new ChatContextCompactor(_estimator, new ContextSummaryWriter()),
            new FixedLimitsResolver(2_200, 256));

        var plan = planner.Plan(null, "small-model", messages, []);

        plan.Fits.ShouldBeTrue();
        plan.Messages[2].Content.ShouldBeSameAs(stored);
        plan.Messages[2].ForModel.Length.ShouldBeLessThan(1_000);
        messages[2].ModelContent.ShouldBeNull();
    }

    [Fact]
    public void ShouldReplaceOldCompleteTurnsWithDeterministicSummary()
    {
        var messages = Enumerable.Range(1, 6).SelectMany(index => new ChatCompletionMessage[]
        {
            new("user", $"request-{index} " + new string((char)('a' + index), 10_000)),
            new("assistant", $"outcome-{index}")
        }).ToArray();

        var first = Planner().Plan(null, "unknown-model", messages, []);
        var second = Planner().Plan(null, "unknown-model", messages, []);

        first.Fits.ShouldBeTrue();
        first.WasCompacted.ShouldBeTrue();
        first.OmittedMessages.ShouldBe(8);
        first.Messages[0].Role.ShouldBe("user");
        first.Messages[0].Content.ShouldContain("request-1");
        first.Messages[0].Content.ShouldContain("outcome-4");
        first.Messages[^2].Content.ShouldStartWith("request-6");
        first.Messages[^2].Content.ShouldBe(messages[^2].Content);
        first.EstimatedInputTokens.ShouldBe(second.EstimatedInputTokens);
        first.OmittedMessages.ShouldBe(second.OmittedMessages);
        first.Messages.ShouldBe(second.Messages);
    }

    [Fact]
    public void ShouldNeverSplitToolCallProtocolGroupsWhenOmittingHistory()
    {
        var messages = new List<ChatCompletionMessage>();
        for (var turn = 1; turn <= 5; turn++)
        {
            messages.Add(new ChatCompletionMessage("user", $"request-{turn} " + new string('x', 12_000)));
            messages.Add(new ChatCompletionMessage("assistant", "",
                [new ChatToolCall($"call-{turn}-1", "read", "{}"), new ChatToolCall($"call-{turn}-2", "read", "{}") ]));
            messages.Add(new ChatCompletionMessage("tool", "ok", ToolCallId: $"call-{turn}-1"));
            messages.Add(new ChatCompletionMessage("tool", "ok", ToolCallId: $"call-{turn}-2"));
            messages.Add(new ChatCompletionMessage("assistant", $"done-{turn}"));
        }

        var plan = Planner().Plan(null, "unknown-model", messages, []);

        plan.WasCompacted.ShouldBeTrue();
        AssertValidToolProtocol(plan.Messages);
    }

    [Fact]
    public void ShouldNotTruncateCurrentUserRequestWhenItCannotFit()
    {
        var request = new string('x', 60_000);

        var plan = Planner().Plan(null, "unknown-model", [new ChatCompletionMessage("user", request)], []);

        plan.Fits.ShouldBeFalse();
        plan.Messages.ShouldHaveSingleItem().Content.ShouldBeSameAs(request);
        plan.OmittedMessages.ShouldBe(0);
    }

    [Fact]
    public async Task PlanAsyncShouldFallBackToLlmSummaryWhenDeterministicCompactionCannotFit()
    {
        // A history that the deterministic compactor still leaves larger than the window: even
        // after projecting tool results and omitting older turns, the projected size is greater
        // than the budget, so the LLM summarizer has to run.
        var messages = Enumerable.Range(1, 8).SelectMany(index => new ChatCompletionMessage[]
        {
            new("user", $"request-{index} " + new string('a', 800)),
            new("assistant", $"outcome-{index} " + new string('b', 800)),
            new("tool", new string('c', 2_000), ToolCallId: $"call-{index}")
        }).ToArray();
        var planner = new ChatContextPlanner(_estimator, new ChatContextCompactor(_estimator, new ContextSummaryWriter()),
            new FixedLimitsResolver(3_200, 256));
        var summarizer = new RecordingSummarizer("Compacted earlier work.");

        var plan = await planner.PlanAsync(null, "small-model", messages, [], summarizer, 800,
            CancellationToken.None);

        summarizer.Calls.ShouldBe(1);
        plan.WasCompacted.ShouldBeTrue();
        plan.Messages.ShouldContain(message => message.Content.StartsWith(
            "Earlier conversation summary (LLM-generated"));
        // The result is assembled from the deterministic projection, so the full tool results must
        // not reappear, and the assembled size is re-verified against the limit before returning.
        plan.Messages.ShouldNotContain(message => message.Role == "tool" && message.ForModel.Length >= 2_000);
        plan.Fits.ShouldBeTrue();
        // Built from unstored messages, the summary has nothing to be kept against.
        plan.HistorySummary.ShouldBeNull();
    }

    [Fact]
    public async Task PlanAsyncShouldOfferAnLlmSummaryOfStoredMessagesForKeeping()
    {
        var ids = Enumerable.Range(0, 16).Select(_ => Guid.NewGuid()).ToArray();
        var messages = Enumerable.Range(0, 8).SelectMany(index => new ChatCompletionMessage[]
        {
            new("user", $"request-{index} " + new string('a', 800), MessageId: ids[index * 2]),
            new("assistant", $"outcome-{index} " + new string('b', 800), MessageId: ids[index * 2 + 1])
        }).ToArray();
        var planner = new ChatContextPlanner(_estimator, new ChatContextCompactor(_estimator, new ContextSummaryWriter()),
            new FixedLimitsResolver(3_200, 256));

        var plan = await planner.PlanAsync(null, "small-model", messages, [], new RecordingSummarizer("Earlier work."), 800,
            CancellationToken.None);

        plan.HistorySummary.ShouldNotBeNull();
        plan.HistorySummary.Text.ShouldBe("Earlier work.");
        var covered = Array.IndexOf(ids, plan.HistorySummary.UpToMessageId);
        // It ends on an answer, so the next request can start again at the question after it.
        (covered % 2).ShouldBe(1);
        plan.HistorySummary.CoveredMessages.ShouldBe(covered + 1);
    }

    [Fact]
    public async Task PlanAsyncShouldCompactTheCurrentTurnWhenOneTurnAloneExceedsTheWindow()
    {
        // A single enormous turn: there are no older turns to summarize, so the completed head of
        // the current turn has to be replaced by a deterministic digest.
        var messages = new List<ChatCompletionMessage> { new("user", "request " + new string('a', 400)) };
        for (var index = 1; index <= 20; index++)
        {
            messages.Add(new ChatCompletionMessage("assistant", $"step-{index}",
                ToolCalls: [new ChatToolCall($"call-{index}", "read_file", "{}")]));
            messages.Add(new ChatCompletionMessage("tool", new string('c', 4_000), ToolCallId: $"call-{index}"));
        }

        var planner = new ChatContextPlanner(_estimator, new ChatContextCompactor(_estimator, new ContextSummaryWriter()),
            new FixedLimitsResolver(3_200, 256));
        var summarizer = new RecordingSummarizer("unused");

        var plan = await planner.PlanAsync(null, "small-model", messages, [], summarizer, 800,
            CancellationToken.None);

        plan.Fits.ShouldBeTrue();
        plan.WasCompacted.ShouldBeTrue();
        plan.OmittedMessages.ShouldBeGreaterThan(0);
        plan.Messages.ShouldContain(message => message.Content.StartsWith("Current turn progress summary"));
        AssertValidToolProtocol(plan.Messages);
    }

    [Fact]
    public async Task PlanAsyncShouldSkipLlmSummaryWhenDeterministicCompactionAlreadyFits()
    {
        var messages = Enumerable.Range(1, 3).SelectMany(index => new ChatCompletionMessage[]
        {
            new("user", $"request-{index} " + new string('a', 50)),
            new("assistant", $"outcome-{index} " + new string('b', 50))
        }).ToArray();
        var planner = new ChatContextPlanner(_estimator, new ChatContextCompactor(_estimator, new ContextSummaryWriter()),
            new ConnectionContextLimitsResolver());
        var summarizer = new RecordingSummarizer("should not run");

        var plan = await planner.PlanAsync(null, "unknown-model", messages, [], summarizer, 800,
            CancellationToken.None);

        plan.Fits.ShouldBeTrue();
        summarizer.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task PlanAsyncShouldReturnDeterministicResultWhenLlmSummarizerReturnsEmpty()
    {
        var messages = Enumerable.Range(1, 8).SelectMany(index => new ChatCompletionMessage[]
        {
            new("user", $"request-{index} " + new string('a', 800)),
            new("assistant", $"outcome-{index} " + new string('b', 800)),
            new("tool", new string('c', 2_000), ToolCallId: $"call-{index}")
        }).ToArray();
        var planner = new ChatContextPlanner(_estimator, new ChatContextCompactor(_estimator, new ContextSummaryWriter()),
            new FixedLimitsResolver(3_200, 256));
        var summarizer = new RecordingSummarizer(string.Empty);

        var plan = await planner.PlanAsync(null, "small-model", messages, [], summarizer, 800,
            CancellationToken.None);

        summarizer.Calls.ShouldBe(1);
        plan.Messages.ShouldNotContain(message => message.Content.StartsWith(
            "Earlier conversation summary (LLM-generated"));
        // Without a usable summary the deterministic path still has to bring the request under the
        // limit rather than return an oversized context.
        plan.Fits.ShouldBeTrue();
        plan.Messages.ShouldContain(message => message.Content.StartsWith(
            "Earlier conversation summary (deterministic"));
    }

    private ChatContextPlanner Planner() => new(_estimator, new ChatContextCompactor(_estimator, new ContextSummaryWriter()),
        new ConnectionContextLimitsResolver());

    private static void AssertValidToolProtocol(IReadOnlyList<ChatCompletionMessage> messages)
    {
        var pending = new Queue<string>();
        foreach (var message in messages)
        {
            if (pending.Count > 0)
            {
                message.Role.ShouldBe("tool");
                message.ToolCallId.ShouldBe(pending.Dequeue());
            }
            else
            {
                message.Role.ShouldNotBe("tool");
            }

            foreach (var call in message.ToolCalls ?? []) pending.Enqueue(call.Id);
        }

        pending.ShouldBeEmpty();
    }

    private sealed class SaturatedEstimator : IContextTokenEstimator
    {
        public long EstimateMessages(IReadOnlyList<ChatCompletionMessage> messages) => long.MaxValue;

        public long EstimateTools(IReadOnlyList<ChatToolDefinition> tools) => long.MaxValue;
    }

    private sealed class FixedLimitsResolver(long contextWindow, long reservedOutput) : IConnectionContextLimitsResolver
    {
        public ResolvedConnectionContextLimits Resolve(ConnectionSettings? connection) =>
            new(contextWindow, ContextLimitSource.Override, reservedOutput, ContextLimitSource.Override);
    }

    private sealed class RecordingSummarizer(string reply) : IContextSummarizer
    {
        private int _calls;
        public int Calls => _calls;

        public Task<string> SummarizeAsync(string prompt, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(reply);
        }
    }
}
