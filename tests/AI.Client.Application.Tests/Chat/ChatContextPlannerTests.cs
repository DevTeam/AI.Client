namespace AI.Client.Application.Tests.Chat;

using System.Text;
using System.Text.Json;
using AI.Client.Application.Chat;
using AI.Client.Contracts.Chat;
using Shouldly;
using Xunit;

public sealed class ChatContextPlannerTests
{
    private readonly ContextTokenEstimator _estimator = new();

    [Fact]
    public void ShouldPassSmallContextWithoutChangingMessages()
    {
        ChatCompletionMessage[] messages = [new("user", "Hello")];
        var planner = new ChatContextPlanner(_estimator);

        var plan = planner.Plan("unknown-model", messages, []);

        plan.Fits.ShouldBeTrue();
        plan.Messages.ShouldBeSameAs(messages);
        plan.WasCompacted.ShouldBeFalse();
        plan.OmittedMessages.ShouldBe(0);
    }

    [Fact]
    public void ShouldSubtractToolDefinitionsFromInputLimit()
    {
        var planner = new ChatContextPlanner(_estimator);
        var withoutTools = planner.Plan("unknown-model", [], []);
        ChatToolDefinition[] tools =
        [
            new("read_file", new string('d', 2_000),
                JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"}}}""").RootElement.Clone())
        ];

        var withTools = planner.Plan("unknown-model", [], tools);

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
        var planner = new ChatContextPlanner(new SaturatedEstimator());

        var plan = planner.Plan("unknown-model", [new ChatCompletionMessage("user", "large")],
            [new ChatToolDefinition("tool", "large", JsonDocument.Parse("{}").RootElement.Clone())]);

        plan.EstimatedInputTokens.ShouldBe(long.MaxValue);
        plan.ToolDefinitionTokens.ShouldBe(long.MaxValue);
        plan.InputLimit.ShouldBe(0);
        plan.Fits.ShouldBeFalse();
    }

    [Fact]
    public void ShouldGiveUnknownModelsConservativeDefaults()
    {
        var plan = new ChatContextPlanner(_estimator).Plan("vendor-specific-model", [], []);

        plan.InputLimit.ShouldBe(27_392);
        plan.ReservedOutputTokens.ShouldBe(4_096);
    }

    private sealed class SaturatedEstimator : IContextTokenEstimator
    {
        public long EstimateMessages(IReadOnlyList<ChatCompletionMessage> messages) => long.MaxValue;

        public long EstimateTools(IReadOnlyList<ChatToolDefinition> tools) => long.MaxValue;
    }
}
