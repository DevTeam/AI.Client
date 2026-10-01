namespace AI.Application.Tests.Chat;

using AI.Application.Chat;
using AI.Application.Tools;
using Shouldly;
using Xunit;

public sealed class ModelInstructionComposerTests
{
    [Fact]
    public void ShouldComposeHiddenInstructionsWithoutChangingOriginalContext()
    {
        var registry = new ModelInstructionRegistry();
        var composer = new ModelInstructionComposer(registry, new ContextTokenEstimator());
        var run = new ToolRunContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true);
        var context = new[] { new ChatCompletionMessage("user", "Visible request") };
        using var scope = registry.Begin(run);
        registry.Upsert(run, new ModelInstruction("low", "Low priority", 1));
        registry.Upsert(run, new ModelInstruction("high", "High priority", 10));

        var result = composer.Compose(run, context);

        context.ShouldHaveSingleItem().Role.ShouldBe("user");
        result.Keys.ShouldBe(["high", "low"]);
        result.Messages.Select(message => message.Role).ShouldBe(["system", "system", "user"]);
        result.Messages[0].Content.ShouldBe("High priority");
        result.EstimatedTokens.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void ShouldAcknowledgeTransientInstructionsAndKeepRunInstructions()
    {
        var registry = new ModelInstructionRegistry();
        var composer = new ModelInstructionComposer(registry, new ContextTokenEstimator());
        var run = new ToolRunContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true);
        using var scope = registry.Begin(run);
        registry.Upsert(run, new ModelInstruction("request", "Once", Lifetime: ModelInstructionLifetime.Request));
        registry.Upsert(run, new ModelInstruction("ack", "Until reply", Lifetime: ModelInstructionLifetime.UntilAcknowledged));
        registry.Upsert(run, new ModelInstruction("run", "Always", Lifetime: ModelInstructionLifetime.Run));

        var result = composer.Compose(run, []);
        composer.Acknowledge(run, result);

        registry.List(run).Select(item => item.Key).ShouldBe(["run"]);
    }

    [Fact]
    public void ShouldPlaceStandingLayersFirstAndOutsideTheRunBudget()
    {
        var registry = new ModelInstructionRegistry();
        var composer = new ModelInstructionComposer(registry, new ContextTokenEstimator());
        var run = new ToolRunContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true);
        using var scope = registry.Begin(run);
        // Larger than the whole run budget on its own: a standing layer is bounded where it is built.
        registry.Upsert(run, new ModelInstruction("project.instructions", new string('p', 6_000), 2,
            Placement: ModelInstructionPlacement.Standing));
        registry.Upsert(run, new ModelInstruction("app.base", "Base", 3, Placement: ModelInstructionPlacement.Standing));
        registry.Upsert(run, new ModelInstruction("run.completion-protocol", "Protocol", 1_000));

        var result = composer.Compose(run, [new ChatCompletionMessage("user", "Hello")]);

        result.Keys.ShouldBe(["app.base", "project.instructions", "run.completion-protocol"]);
        result.Messages[^1].Role.ShouldBe("user");
    }

    [Fact]
    public void ShouldSendStepGuidanceAfterTheConversationSoTheCachedPrefixStays()
    {
        var registry = new ModelInstructionRegistry();
        var composer = new ModelInstructionComposer(registry, new ContextTokenEstimator());
        var run = new ToolRunContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true);
        using var scope = registry.Begin(run);
        registry.Upsert(run, new ModelInstruction("run.protocol", "Protocol", 1_000));
        registry.Upsert(run, new ModelInstruction("run.stalled", "Finish now", 970, ModelInstructionLifetime.Request));
        registry.Upsert(run, new ModelInstruction("run.active-skill", "Active skill", 880,
            Placement: ModelInstructionPlacement.Trailing));
        ChatCompletionMessage[] context = [new("user", "Hello")];

        var result = composer.Compose(run, context);

        result.Messages.Select(message => message.Content).ShouldBe(["Protocol", "Hello"]);
        var note = result.TrailingMessages.ShouldHaveSingleItem();
        note.Role.ShouldBe("user");
        note.Content.ShouldBe(ModelInstructionComposer.TrailingPrefix + "Finish now\n\nActive skill");
        result.Keys.ShouldBe(["run.protocol", "run.stalled", "run.active-skill"]);
    }
}
