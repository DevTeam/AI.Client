namespace AI.Client.Application.Tests.Chat;

using System.Text;
using System.Text.Json;
using AI.Client.Application.Chat;
using AI.Client.Contracts.Chat;
using AI.Client.Contracts.Settings;
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
        var planner = new ChatContextPlanner(saturated, new ChatContextCompactor(saturated),
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

    private ChatContextPlanner Planner() => new(_estimator, new ChatContextCompactor(_estimator),
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
}
