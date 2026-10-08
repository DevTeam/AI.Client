namespace AI.Application.Tests.Chat;

using System.Text.Json;
using AI.Application.Chat;
using AI.Application.Instructions;
using AI.Application.Tools;
using AI.Application.Usage;
using AI.Contracts.Chat;
using AI.Contracts.Instructions;
using AI.Contracts.Settings;
using AI.Contracts.Tools;
using AI.Contracts.Usage;
using Shouldly;
using Moq;
using Xunit;

public sealed class AdaptiveContextPolicyTests
{
    private readonly ContextTokenEstimator _estimator = new();
    private AdaptiveContextPolicy Policy() => new(_estimator, new ConnectionContextLimitsResolver());
    private static ConnectionSettings Connection(long window, long reserve = 1_000) =>
        new(Guid.NewGuid(), "Test", "https://example.test/v1", "model", true, true, false,
            ContextWindowTokens: window, ReservedOutputTokens: reserve);

    [Theory]
    [InlineData(4_096)]
    [InlineData(8_192)]
    [InlineData(16_384)]
    [InlineData(32_768)]
    [InlineData(131_072)]
    public void ShouldFitTheCompleteRequestWithAnOversizedCatalogue(long window)
    {
        var connection = Connection(window);
        var policy = Policy();
        var preview = policy.PrepareStanding(Preview(), connection, true);
        var messages = preview.Layers.Where(layer => layer.Content.Length > 0)
            .Select(layer => new ChatCompletionMessage("system", layer.Content))
            .Append(new ChatCompletionMessage("user", "Read a file")).ToArray();
        var search = Tool("tool_search", "Find capabilities", app: true);
        var tools = Enumerable.Range(0, 100).Select(index => Tool($"tool_{index}", new string('x', 600))).Prepend(search).ToArray();

        var selection = policy.Choose(connection, "Read a file", messages, tools);
        var plan = new ChatContextPlanner(_estimator, new ChatContextCompactor(_estimator, new ContextSummaryWriter(new ContextTokenEstimator(), new ToolResultContextProjector(), new AdaptiveContextPolicy(new ContextTokenEstimator(), new AI.Contracts.Settings.ConnectionContextLimitsResolver())), new ToolResultContextProjector()),
            new ConnectionContextLimitsResolver(), policy).Plan(connection, "model", messages,
                selection.Tools.Select(tool => tool.ModelDefinition).ToArray());

        plan.Fits.ShouldBeTrue();
        selection.Tools.ShouldContain(search);
        selection.SelectedTokens.ShouldBeLessThanOrEqualTo(selection.BudgetTokens);
        selection.BudgetTokens.ShouldBeLessThanOrEqualTo(policy.Resolve(connection).ToolTokens);
        preview.TotalTokens.ShouldBeLessThanOrEqualTo(policy.Resolve(connection).InstructionTokens);
        preview.Layers.Single(layer => layer.Key == StandingInstructions.ProjectKey).Content.ShouldBe("Keep project rules.");
    }

    [Fact]
    public void ShouldReduceBudgetsWhenTheOutputReserveGrows()
    {
        var normal = Policy().Resolve(Connection(8_192, 1_000));
        var largeOutput = Policy().Resolve(Connection(8_192, 4_000));
        largeOutput.ToolTokens.ShouldBeLessThan(normal.ToolTokens);
        largeOutput.InstructionTokens.ShouldBeLessThan(normal.InstructionTokens);
        largeOutput.UsableTokens.ShouldBeLessThan(normal.UsableTokens);
        var exhausted = Policy().Resolve(Connection(1_024, 1_000));
        exhausted.ToolTokens.ShouldBe(0);
        exhausted.InstructionTokens.ShouldBe(0);
        Policy().Resolve(Connection(32_768, 1_000)).Compact.ShouldBeTrue();
        Policy().Resolve(Connection(32_768, 20_000)).Compact.ShouldBeTrue();
    }

    [Fact]
    public void ShouldExpandInstructionsAndToolsWithTheAvailableWindow()
    {
        var policy = Policy();
        var source = Preview();
        var small = policy.PrepareStanding(source, Connection(8_192), true);
        var large = policy.PrepareStanding(source, Connection(131_072), true);
        small.Profile.ShouldBe("Compact");
        large.Profile.ShouldBe("Full");
        small.TotalTokens.ShouldBeLessThan(large.TotalTokens);
        small.Layers.Single(layer => layer.Key == StandingInstructions.SkillsKey).Content.ShouldContain("mcp_app__skill_search");
        large.Layers.Single(layer => layer.Key == StandingInstructions.SkillsKey).Content.ShouldBe(source.Layers[^1].Content);
        var tools = Enumerable.Range(0, 100).Select(index => Tool($"tool_{index}", new string('x', 500))).ToArray();
        var smallTools = policy.Choose(Connection(8_192), "request", [], tools);
        var largeTools = policy.Choose(Connection(131_072), "request", [], tools);
        smallTools.Tools.Count.ShouldBeLessThan(largeTools.Tools.Count);
        source.Layers[0].Content.ShouldStartWith("Full guide");
    }

    [Fact]
    public void ShouldUseThePreparedCompactInstructionWithoutClippingItsText()
    {
        var source = new ModelInstruction("run.protocol", new string('f', 2_000), Required: true,
            CompactContent: "Complete the authorized task and preserve the call protocol.");
        var small = Policy().SelectInstructions([source], Connection(8_192)).ShouldHaveSingleItem();
        var large = Policy().SelectInstructions([source], Connection(131_072)).ShouldHaveSingleItem();
        small.Content.ShouldBe(source.CompactContent);
        large.Content.ShouldBe(source.Content);
        source.Content.ShouldBe(new string('f', 2_000));
    }

    [Fact]
    public void ShouldPreserveRequiredInstructionsAndSpendOptionalBudgetByPriority()
    {
        var mandatory = new ModelInstruction("run.protocol", "Always finish the protocol.", Required: true);
        var high = new ModelInstruction("high", new string('h', 400), 100);
        var low = new ModelInstruction("low", new string('l', 1_000), 1, ModelInstructionLifetime.Request);
        var chosen = Policy().SelectInstructions([low, high, mandatory], Connection(8_192));
        chosen.ShouldContain(mandatory);
        chosen.ShouldContain(high);
        chosen.ShouldNotContain(low);
    }

    [Fact]
    public void ShouldPreserveOversizedUserInstructionsAndExplainTheFailure()
    {
        var policy = Policy();
        var connection = Connection(4_096);
        var source = Preview(new string('p', 20_000));
        var prepared = policy.PrepareStanding(source, connection, true);
        prepared.Layers.Single(layer => layer.Key == StandingInstructions.ProjectKey).Content.ShouldBe(source.Layers[1].Content);
        var messages = prepared.Layers.Where(layer => layer.Content.Length > 0).Select(layer => new ChatCompletionMessage("system", layer.Content))
            .Append(new ChatCompletionMessage("user", "Hello")).ToArray();
        var plan = new ChatContextPlanner(_estimator, new ChatContextCompactor(_estimator, new ContextSummaryWriter(new ContextTokenEstimator(), new ToolResultContextProjector(), new AdaptiveContextPolicy(new ContextTokenEstimator(), new AI.Contracts.Settings.ConnectionContextLimitsResolver())), new ToolResultContextProjector()),
            new ConnectionContextLimitsResolver(), policy).Plan(connection, "model", messages, []);
        plan.Fits.ShouldBeFalse();
        var error = new ContextWindowExceededException(plan);
        error.Message.ShouldContain("Context window 4096");
        error.Message.ShouldContain("history compaction cannot make this request fit");
    }

    [Fact]
    public async Task ShouldNotAskAnLlmToCompactAnUnavoidableFailure()
    {
        var policy = Policy();
        var summarizer = new Mock<IContextSummarizer>(MockBehavior.Strict);
        var messages = new ChatCompletionMessage[] { new("system", new string('s', 20_000)),
            new("user", "Earlier question"), new("assistant", "Earlier answer"), new("user", "Current question") };
        var plan = await new ChatContextPlanner(_estimator, new ChatContextCompactor(_estimator, new ContextSummaryWriter(new ContextTokenEstimator(), new ToolResultContextProjector(), new AdaptiveContextPolicy(new ContextTokenEstimator(), new AI.Contracts.Settings.ConnectionContextLimitsResolver())), new ToolResultContextProjector()),
            new ConnectionContextLimitsResolver(), policy).PlanAsync(Connection(4_096), "model", messages, [], summarizer.Object,
                800, TestContext.Current.CancellationToken);
        plan.Fits.ShouldBeFalse();
        plan.Messages.ShouldContain(message => message.Content == "Current question");
        summarizer.VerifyNoOtherCalls();
    }

    [Fact]
    public void ShouldPreferDiscoveryAndQuestionsToExpensiveAppManagementTools()
    {
        var search = Tool("tool_search", "Search tools", app: true);
        var ask = Tool("ask_user", "Ask the user", app: true);
        var management = Enumerable.Range(0, 20).Select(index => Tool($"app_manage_{index}", new string('m', 1_000), app: true)).ToArray();
        var selection = Policy().Choose(Connection(4_096), "Manage projects", [], [.. management, ask, search]);
        selection.Tools.ShouldContain(search);
        selection.Tools.ShouldContain(ask);
        selection.Tools.ShouldNotContain(tool => management.Contains(tool));
    }

    [Fact]
    public void ShouldNotLetDiscoveredToolsBypassTheBudget()
    {
        var huge = Tool("huge", new string('h', 20_000));
        var small = Tool("small", "Operation");
        var selection = Policy().Choose(Connection(8_192), "huge", [], [huge, small],
            new HashSet<string>([huge.ModelDefinition.Name]));
        selection.Tools.ShouldNotContain(huge);
        selection.SelectedTokens.ShouldBeLessThanOrEqualTo(selection.BudgetTokens);
    }

    [Fact]
    public void ShouldProtectCurrentProtocolButNotToolsFromOlderUserTurns()
    {
        var old = Tool("old", new string('o', 10_000));
        var current = Tool("current", new string('c', 3_000));
        ChatCompletionMessage[] messages = [new("user", "old task"),
            new("assistant", "", [new ChatToolCall("old-call", old.ModelDefinition.Name, "{}")]),
            new("tool", "done", ToolCallId: "old-call"), new("user", "new task"),
            new("assistant", "", [new ChatToolCall("current-call", current.ModelDefinition.Name, "{}")]),
            new("tool", "done", ToolCallId: "current-call")];
        var selection = Policy().Choose(Connection(8_192), "new task", messages, [old, current]);
        selection.Tools.ShouldContain(current);
        selection.Tools.ShouldNotContain(old);
        // Only protocol-required definitions may exceed the schema share; the final planner still validates them.
        selection.SelectedTokens.ShouldBeGreaterThan(selection.BudgetTokens);
        messages[2].Content.ShouldBe("done");
    }

    [Fact]
    public void ShouldKeepTheTurnsToolsWhenAMessageJoinsTheTurn()
    {
        var submit = Tool("app_runs", new string('r', 3_000), app: true);
        var read = Tool("app_read", new string('a', 3_000), app: true);
        var others = Enumerable.Range(0, 20).Select(index => Tool($"other_{index}", new string('x', 1_000))).ToArray();
        // The charter the lead submitted joins its own turn; the history is past the window.
        ChatCompletionMessage[] messages = [new("user", "/team " + new string('u', 400_000)),
            new("assistant", "", [new ChatToolCall("call-1", submit.ModelDefinition.Name, "{}")]),
            new("tool", "queued", ToolCallId: "call-1"), new("user", "Team charter", JoinsTurn: true),
            new("assistant", "", [new ChatToolCall("call-2", read.ModelDefinition.Name, "{}")]),
            new("tool", "messages", ToolCallId: "call-2")];

        var selection = Policy().Choose(Connection(131_072), "/team", messages, [submit, read, .. others]);

        selection.Tools.ShouldContain(submit);
        selection.Tools.ShouldContain(read);
    }

    [Fact]
    public void ShouldKeepAFloorOfToolsWhenHistoryAloneExceedsTheWindow()
    {
        var search = Tool("tool_search", "Find capabilities", app: true);
        var ask = Tool("ask_user", "Ask the person", app: true);
        var others = Enumerable.Range(0, 20).Select(index => Tool($"other_{index}", new string('x', 200))).ToArray();
        // The planner summarizes such a history after the choice; the turn must still have tools then.
        ChatCompletionMessage[] messages = [new("user", "request"), new("assistant", new string('h', 600_000))];

        var selection = Policy().Choose(Connection(131_072), "request", messages, [search, ask, .. others]);

        selection.BudgetTokens.ShouldBeGreaterThan(0);
        selection.Tools.ShouldContain(search);
        selection.Tools.ShouldContain(ask);
        selection.SelectedTokens.ShouldBeLessThanOrEqualTo(selection.BudgetTokens);
    }

    [Fact]
    public void ShouldReleaseProtocolDefinitionsAfterACheckpointRemovesTheirCalls()
    {
        var policy = Policy();
        var connection = Connection(8_192);
        var completed = Tool("completed", new string('c', 3_000));
        var search = Tool("tool_search", "Find capabilities", app: true);
        ChatCompletionMessage[] before = [new("user", "Investigate"),
            new("assistant", "", [new ChatToolCall("call-1", completed.ModelDefinition.Name, "{}")]),
            new("tool", "Evidence", ToolCallId: "call-1")];
        var previous = policy.Choose(connection, "Investigate", before, [completed, search]);
        previous.Tools.ShouldContain(completed);
        ChatCompletionMessage[] after = [new("user", "Investigate"), new("user", "Checkpoint: finished the inspection.")];
        var next = policy.Choose(connection, "Investigate", after, [completed, search], previousTools: previous.Tools);
        next.Tools.ShouldNotContain(completed);
        next.Tools.ShouldContain(search);
        next.SelectedTokens.ShouldBeLessThanOrEqualTo(next.BudgetTokens);
    }

    [Fact]
    public void ShouldReserveSpaceForCurrentTextAndTrailingGuidanceBeforeSelectingTools()
    {
        var tools = Enumerable.Range(0, 20).Select(index => Tool($"tool_{index}", new string('x', 600))).ToArray();
        var policy = Policy();
        var connection = Connection(8_192);
        var ordinary = policy.Choose(connection, "request", [], tools);
        var pressured = policy.Choose(connection, "request", [new("system", new string('s', 7_000)), new("user", new string('u', 2_000))],
            tools, trailingInstructionTokens: 1_000);
        pressured.SelectedTokens.ShouldBeLessThan(ordinary.SelectedTokens);
        pressured.BudgetTokens.ShouldBeLessThan(ordinary.BudgetTokens);
    }

    [Fact]
    public void ShouldKeepTheExactSchemaAndCachedPrefixAsTheTurnGrows()
    {
        var policy = Policy();
        var connection = Connection(16_384);
        var tools = Enumerable.Range(0, 30).Select(index => Tool($"tool_{index}", "Generic operation " + new string('x', 500))).ToArray();
        ChatCompletionMessage[] firstMessages = [new("system", "Stable instructions"), new("user", "Investigate")];
        var first = policy.Choose(connection, "Investigate", firstMessages, tools);
        ChatCompletionMessage[] nextMessages = [.. firstMessages, new("assistant", "Working"), new("tool", "New evidence")];
        var next = policy.Choose(connection, "Investigate", nextMessages, tools, previousTools: first.Tools);
        next.Tools.ShouldBe(first.Tools);

        var tracker = new PromptPrefixTracker(_estimator);
        var key = new PromptPrefixKey(Guid.NewGuid(), Guid.NewGuid(), TokenUsagePurpose.Answer);
        tracker.Compare(key, tracker.Shape(Request(firstMessages, first.Tools))).ShouldBeNull();
        var prefix = tracker.Compare(key, tracker.Shape(Request(nextMessages, next.Tools))).ShouldNotBeNull();
        prefix.Change.ShouldBeNull();
        prefix.ReusableTokens.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void ShouldAddADiscoveredCapabilityOnceAndKeepItsSchemaStable()
    {
        var policy = Policy();
        var connection = Connection(8_192);
        var tools = Enumerable.Range(0, 30).Select(index => Tool($"tool_{index}", new string('x', 500))).ToArray();
        var first = policy.Choose(connection, "request", [], tools);
        var discovered = tools.First(tool => !first.Tools.Contains(tool));
        var pins = new HashSet<string>([discovered.ModelDefinition.Name]);
        var next = policy.Choose(connection, "request", [], tools, pins, first.Tools);
        var again = policy.Choose(connection, "request", [], tools, pins, next.Tools);
        next.Tools.ShouldContain(discovered);
        again.Tools.ShouldBe(next.Tools);
        next.SelectedTokens.ShouldBeLessThanOrEqualTo(next.BudgetTokens);
        discovered.ModelDefinition.InputSchema.GetRawText().ShouldBe("{}");
    }

    [Fact]
    public void ShouldEvictOptionalToolsUnderPressureAndKeepSurvivorsInOrder()
    {
        var policy = Policy();
        var connection = Connection(8_192);
        var tools = Enumerable.Range(0, 30).Select(index => Tool($"tool_{index}", new string('x', 500))).ToArray();
        var first = policy.Choose(connection, "request", [], tools);
        var next = policy.Choose(connection, "request", [new("system", new string('s', 10_800)), new("user", "request")],
            tools, previousTools: first.Tools);
        next.Tools.Count.ShouldBeLessThan(first.Tools.Count);
        next.SelectedTokens.ShouldBeLessThanOrEqualTo(next.BudgetTokens);
        next.Tools.ShouldBe(first.Tools.Where(next.Tools.Contains));
    }

    [Fact]
    public void ShouldCombineBatchedSearchesAndReleasePrioritiesAfterSelection()
    {
        var catalog = new ToolCatalogRegistry();
        var run = new ToolRunContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true);
        using var scope = catalog.Begin(run);
        var alpha = Tool("alpha", "alpha");
        var beta = Tool("beta", "beta");
        catalog.Update(run, [alpha, beta]);
        catalog.SearchAndPin(run, "alpha", 1);
        catalog.SearchAndPin(run, "beta", 1);
        catalog.ConsumePinned(run).ShouldBe(new HashSet<string>([alpha.ModelDefinition.Name, beta.ModelDefinition.Name]));
        catalog.GetPinned(run).ShouldBeEmpty();
        catalog.SearchAndPin(run, "beta", 1);
        catalog.ConsumePinned(run).ShouldBe(new HashSet<string>([beta.ModelDefinition.Name]));
    }

    private ModelContextPreview Preview(string project = "Keep project rules.")
    {
        ModelContextLayer Layer(string key, string text) => new(key, key, [], text,
            _estimator.EstimateMessages([new ChatCompletionMessage("system", text)]), 24_576, false);
        var layers = new[] { Layer(StandingInstructions.BaseKey, "Full guide " + new string('b', 4_000)),
            Layer(StandingInstructions.ProjectKey, project), Layer(StandingInstructions.MemoryKey, "Memory\n- [one] A fact"),
            Layer(StandingInstructions.SkillsKey, "Skill catalog\n" + string.Join('\n', Enumerable.Range(0, 100).Select(index => $"- skill-{index}: A useful procedure " + new string('d', 120)))) };
        return new(layers, layers.Sum(layer => layer.Tokens));
    }

    private static AgentTool Tool(string name, string description, bool app = false)
    {
        var schema = JsonDocument.Parse("{}").RootElement.Clone();
        var definition = new ChatToolDefinition((app ? ToolRef.AppPrefix : ToolRef.BuiltInPrefix) + name, description, schema);
        return new(definition, ToolDescriptor.Basic(name, name, description, schema), Guid.NewGuid(), name, name);
    }

    private static ChatCompletionRequest Request(IReadOnlyList<ChatCompletionMessage> messages, IReadOnlyList<AgentTool> tools) =>
        new("https://example.test/v1", "model", null, "Investigate", ContextMessages: messages,
            Tools: tools.Select(tool => tool.ModelDefinition).ToArray());

    [Theory]
    [InlineData(32_768)]
    [InlineData(131_072)]
    [InlineData(250_000)]
    public void ShouldRetainEveryOfferedToolDuringProgressiveDiscovery(long window)
    {
        var policy = Policy();
        var connection = Connection(window);
        var tools = Enumerable.Range(0, 45).Select(index => Tool($"operation_{index}", new string('x', 1_000))).ToArray();
        var initial = policy.Choose(connection, "request", [new("user", "request")], tools);
        initial.SelectedTokens.ShouldBeLessThanOrEqualTo(initial.BudgetTokens * 4 / 5);
        var previous = initial;
        for (var step = 0; step < 3; step++)
        {
            var discovered = tools.First(tool => !previous.Tools.Contains(tool));
            var cost = _estimator.EstimateTools([discovered.ModelDefinition]);
            if (previous.SelectedTokens + cost > previous.BudgetTokens) break;
            var pins = new HashSet<string>([discovered.ModelDefinition.Name]);
            var next = policy.Choose(connection, "request", [new("user", "request")], tools, pins, previous.Tools);
            next.Tools.Take(previous.Tools.Count).ShouldBe(previous.Tools);
            next.Tools.ShouldContain(discovered);
            next.Reason.ShouldBe("expanded");
            next.AddedCount.ShouldBe(1);
            next.RemovedCount.ShouldBe(0);
            next.Reordered.ShouldBeFalse();
            next.SelectedTokens.ShouldBeLessThanOrEqualTo(next.BudgetTokens);
            policy.Choose(connection, "another query", [new("user", "request")], tools, previousTools: next.Tools).Tools.ShouldBe(next.Tools);
            previous = next;
        }
        previous.Tools.Count.ShouldBeGreaterThan(initial.Tools.Count);
    }

    [Fact]
    public void ShouldUseLargeWindowHeadroomInsteadOfTheFormerSixThousandTokenCeiling()
    {
        var policy = Policy();
        var tools = Enumerable.Range(0, 49).Select(index => Tool($"operation_{index}", new string('x', 1_000))).ToArray();
        var connection = Connection(250_000, 10_000);
        var selection = policy.Choose(connection, "request", [new("system", new string('s', 38_000)), new("user", "request")], tools);
        selection.SelectedTokens.ShouldBeGreaterThan(6_000);
        selection.Tools.Count.ShouldBeGreaterThan(16);
        var smaller = policy.Choose(Connection(32_768), "request", [], tools);
        selection.BudgetTokens.ShouldBeGreaterThan(smaller.BudgetTokens);
    }

    [Fact]
    public void ShouldKeepASmallWorkingSetWhenASkillPinsItsTools()
    {
        var policy = Policy();
        var working = new[]
        {
            Tool("app_read", "Read application state", app: true),
            Tool("ask_user", "Ask for missing choices", app: true),
            Tool("skill_search", "Find a skill", app: true),
            Tool("tool_search", "Find a tool", app: true),
            Tool("run_skill", "Run a skill", app: true)
        };
        var others = Enumerable.Range(0, 35).Select(index => Tool($"operation_{index}", "Unrelated operation")).ToArray();
        var pins = working.Select(tool => tool.ModelDefinition.Name).ToHashSet(StringComparer.Ordinal);

        var selection = policy.Choose(Connection(250_000, 10_000), "Configure project", [], [.. working, .. others], pins);

        selection.Tools.Count.ShouldBe(8);
        foreach (var tool in working) selection.Tools.ShouldContain(tool);
        selection.Tools.ShouldContain(tool => tool.OriginalName == "tool_search");
    }

    [Fact]
    public void ShouldReduceTheSchemaBudgetWhenConversationHistoryConsumesHeadroom()
    {
        var policy = Policy();
        var connection = Connection(32_768);
        var tools = Enumerable.Range(0, 30).Select(index => Tool($"operation_{index}", new string('x', 1_000))).ToArray();
        ChatCompletionMessage[] start = [new("system", "Stable"), new("user", "request")];
        var first = policy.Choose(connection, "request", start, tools);
        ChatCompletionMessage[] history = [.. start, new("assistant", new string('h', 57_000))];
        var next = policy.Choose(connection, "request", history, tools, previousTools: first.Tools);
        next.BudgetTokens.ShouldBeLessThan(first.BudgetTokens);
        next.Reason.ShouldBe("pressure");
        next.RemovedCount.ShouldBeGreaterThan(0);
        next.Tools.Count.ShouldBeLessThan(first.Tools.Count);
        next.SelectedTokens.ShouldBeLessThanOrEqualTo(next.BudgetTokens);
        next.Tools.ShouldBe(first.Tools.Where(next.Tools.Contains));
        policy.Choose(connection, "request", history, tools, previousTools: next.Tools).Tools.ShouldBe(next.Tools);
    }

    [Fact]
    public void ShouldReportPermissionRemovalAndSchemaChangesWithoutKeepingStaleDefinitions()
    {
        var policy = Policy();
        var connection = Connection(32_768);
        var read = Tool("read", "Read files");
        var write = Tool("write", "Write files");
        var first = policy.Choose(connection, "files", [], [read, write]);
        var updatedRead = read with { ModelDefinition = read.ModelDefinition with { Description = "Read permitted files" } };
        var next = policy.Choose(connection, "files", [], [updatedRead], previousTools: first.Tools);
        next.Tools.ShouldBe([updatedRead]);
        next.Reason.ShouldBe("catalog_changed");
        next.RemovedCount.ShouldBe(1);
        next.DefinitionChangedCount.ShouldBe(1);
        next.AddedCount.ShouldBe(0);
        next.Reordered.ShouldBeFalse();
    }

    [Fact]
    public void ShouldAppendDiscoveredToolsBeyondTheOpportunisticCountWhileTheyFit()
    {
        var policy = Policy();
        var connection = Connection(32_768);
        var tools = Enumerable.Range(0, 25).Select(index => Tool($"operation_{index}", "Operation")).ToArray();
        var first = policy.Choose(connection, "request", [], tools);
        first.Tools.Count.ShouldBe(policy.Resolve(connection).MaximumTools);
        var discovered = tools.First(tool => !first.Tools.Contains(tool));
        var next = policy.Choose(connection, "request", [], tools,
            new HashSet<string>([discovered.ModelDefinition.Name]), first.Tools);
        next.Tools.ShouldBe([.. first.Tools, discovered]);
        next.SelectedTokens.ShouldBeLessThanOrEqualTo(next.BudgetTokens);
        policy.Choose(connection, "request", [], tools, previousTools: next.Tools).Tools.ShouldBe(next.Tools);
    }
}
