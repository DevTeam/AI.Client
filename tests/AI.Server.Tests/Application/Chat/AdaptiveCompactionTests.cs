namespace AI.Application.Tests.Chat;

using System.Text.Json;
using AI.Application.Chat;
using AI.Application.Tools;
using AI.Application.Usage;
using AI.Contracts.Chat;
using AI.Contracts.Settings;
using AI.Contracts.Tools;
using AI.Contracts.Usage;
using Shouldly;
using Xunit;

public sealed class AdaptiveCompactionTests
{
    private readonly ContextTokenEstimator _estimator = new();
    private readonly ToolResultContextProjector _projector = new();

    [Theory]
    [InlineData(4_096, 1_000)]
    [InlineData(8_192, 1_000)]
    [InlineData(32_768, 4_096)]
    [InlineData(131_072, 8_192)]
    public void ShouldUseTheSameAllowanceForEveryCompactionThreshold(long window, long output)
    {
        var policy = Policy();
        var connection = Connection(window, output);
        var budget = policy.ResolveCompaction(connection, 123, 77);
        budget.InputLimit.ShouldBe(policy.Resolve(connection).UsableTokens - 123);
        budget.MessageLimit.ShouldBe(budget.InputLimit - 77);
        budget.AheadThresholdTokens.ShouldBe(budget.MessageLimit * 7 / 10);
        budget.TargetTokens.ShouldBe(budget.MessageLimit * 4 / 5);
        budget.MinimumGainTokens.ShouldBe(Math.Max(1, budget.MessageLimit / 10));
        policy.ResolveCompaction(connection, 123, 77, false).TargetTokens.ShouldBe(budget.MessageLimit);
        policy.ShouldCompactAhead(budget, budget.AheadThresholdTokens - 1, 0).ShouldBeFalse();
        policy.ShouldCompactAhead(budget, budget.AheadThresholdTokens, 0).ShouldBeTrue();
        var retry = budget.AheadThresholdTokens + budget.RetryGrowthTokens;
        policy.ShouldCompactAhead(budget, retry - 1, retry).ShouldBeFalse();
        policy.ShouldCompactAhead(budget, retry, retry).ShouldBeTrue();
    }

    [Fact]
    public void ShouldDisableAheadCompactionWhenMandatoryCostsExhaustTheWindow()
    {
        var policy = Policy();
        var budget = policy.ResolveCompaction(Connection(4_096), long.MaxValue, long.MaxValue);
        budget.MessageLimit.ShouldBe(0);
        budget.TargetTokens.ShouldBe(0);
        policy.ShouldCompactAhead(budget, long.MaxValue, 0).ShouldBeFalse();
    }

    [Fact]
    public void ShouldMeasureActualSavingsIncludingSummaryFraming()
    {
        var policy = Policy();
        ChatCompletionMessage[] before = [new("user", new string('x', 1_000))];
        ChatCompletionMessage[] small = [new("user", "Summary: resolved.")];
        ChatCompletionMessage[] inflated = [new("user", new string('x', 1_001))];
        var gain = _estimator.EstimateMessages(before) - _estimator.EstimateMessages(small);
        policy.ShouldAcceptCompaction(before, small, gain).ShouldBeTrue();
        policy.ShouldAcceptCompaction(before, small, gain + 1).ShouldBeFalse();
        policy.ShouldAcceptCompaction(before, before, 0).ShouldBeFalse();
        policy.ShouldAcceptCompaction(before, inflated, 0).ShouldBeFalse();
    }

    [Fact]
    public void ShouldIncreaseSafetyOnlyForReportedUsageOfTheSameConnectionEndpointAndModel()
    {
        var samples = new ContextEstimateSamples();
        var policy = new AdaptiveContextPolicy(_estimator, new ConnectionContextLimitsResolver(), samples);
        var connection = Connection(8_192);
        var request = new ChatCompletionRequest(connection.BaseUrl, connection.Model, null, "Hello", connection.Id);
        var estimate = _estimator.EstimateMessages([new ChatCompletionMessage("user", request.Message)]);
        var original = policy.Resolve(connection);
        policy.ObserveInputUsage(request, estimate + 900);
        policy.Resolve(connection).OverheadTokens.ShouldBe(original.OverheadTokens + 900);
        policy.Resolve(connection).UsableTokens.ShouldBe(original.UsableTokens - 900);
        policy.Resolve(connection with { Id = Guid.NewGuid() }).ShouldBe(original);
        policy.Resolve(connection with { Model = "another-model" }).ShouldBe(original);
        policy.Resolve(connection with { BaseUrl = "https://another.test/v1" }).ShouldBe(original);
        policy.ObserveInputUsage(request, 0);
        samples.Read(connection.Id, connection.BaseUrl, connection.Model).Count.ShouldBe(1);
        for (var index = 0; index < 16; index++) policy.ObserveInputUsage(request, estimate - 1);
        samples.Read(connection.Id, connection.BaseUrl, connection.Model).Count.ShouldBe(16);
        policy.Resolve(connection).OverheadTokens.ShouldBe(original.OverheadTokens);
    }

    [Theory]
    [InlineData("stdout")]
    [InlineData("stderr")]
    public void ShouldRetainProcessFailuresInTheMiddleOfLargeOutput(string stream)
    {
        var fields = new Dictionary<string, object>
        {
            [stream] = new string('a', 3_000) + "\nC:/repo/build.cs:42 error CS1001: Missing identifier\n" + new string('z', 3_000),
            ["exitCode"] = 1, ["timedOut"] = false
        };
        var content = JsonSerializer.Serialize(fields);
        var projected = _projector.Project(content, "process_run", 750, 250);
        projected.ShouldContain("exitCode: 1");
        projected.ShouldContain("error CS1001");
        projected.Length.ShouldBeLessThan(1_500);
        _projector.Project(projected, "process_run", 384, 128).ShouldBeSameAs(projected);
    }

    [Fact]
    public void ShouldRetainFilePathsAndErrorsOutsideTheExcerpts()
    {
        var content = JsonSerializer.Serialize(new
        {
            content = new string('a', 5_000), path = "C:/repo/important.cs", error = "Access denied", tail = new string('z', 5_000)
        });
        var projected = _projector.Project(content, "read_file", 384, 128);
        projected.ShouldContain("path: C:/repo/important.cs");
        projected.ShouldContain("error: Access denied");
        projected.Length.ShouldBeLessThan(1_000);
    }

    [Fact]
    public void ShouldPreservePlainTextDiagnosticsAndHandleMalformedJson()
    {
        var content = "{broken\n" + new string('a', 5_000) + "\nFAIL: check invariant at src/test.cs:15\n" + new string('z', 5_000);
        _projector.Project(content, "unknown", 750, 250).ShouldContain("FAIL: check invariant");
    }

    [Fact]
    public void ShouldExtractDiagnosticsFromMcpTextBlocks()
    {
        var content = JsonSerializer.Serialize(new[] { new { type = "text", text = new string('a', 4_000)
            + "\nerror: cannot read C:/repo/file.cs\n" + new string('z', 4_000) } });
        _projector.Project(content, "read", 750, 250).ShouldContain("error: cannot read C:/repo/file.cs");
    }

    [Fact]
    public void ShouldTreatUserAuthoredSummaryTextAsTheCurrentQuestion()
    {
        var question = HistoryCheckpointService.SummaryPrefix + new string('x', 8_000);
        ChatCompletionMessage[] context = [new("user", "Earlier"), new("assistant", new string('a', 4_000)),
            new("user", question), new("user", "Synthetic progress", IsContextSummary: true),
            new("assistant", "", [new("latest", "read", "{}")]), new("tool", "evidence", ToolCallId: "latest")];
        var plan = Planner(Policy()).Plan(Connection(4_096), "model", context, []);
        plan.Fits.ShouldBeFalse();
        plan.Messages.ShouldContain(message => message.Content == question && !message.IsContextSummary);
        AssertProtocol(plan.Messages);
    }

    [Fact]
    public async Task ShouldBoundMultilingualSummariesUsingTheSharedEstimator()
    {
        var writer = new ContextSummaryWriter(_estimator, _projector);
        var summary = await writer.WriteAsync([new ChatCompletionMessage("user", "Source")], 256,
            new Summarizer(string.Concat(Enumerable.Repeat("Ошибка 🚀 中文 ", 400))), TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        _estimator.EstimateMessages([new ChatCompletionMessage("user", summary.Text)]).ShouldBeLessThanOrEqualTo(256);
        char.IsHighSurrogate(summary.Text[^2]).ShouldBeFalse();
    }

    [Fact]
    public async Task ShouldCompactCompletedStepsWithoutASummarizerAndKeepTheLatestParallelExchange()
    {
        var context = new List<ChatCompletionMessage> { new("system", "Rules"), new("user", "Keep this exact question") };
        for (var index = 0; index < 30; index++)
        {
            context.Add(new("assistant", "Completed step", [new ChatToolCall($"call-{index}", "read", "{}") ]));
            context.Add(new("tool", new string('x', 1_000), ToolCallId: $"call-{index}"));
        }
        context.Add(new("assistant", "", [new("latest-a", "read", "{}"), new("latest-b", "read", "{}") ]));
        context.Add(new("tool", "Latest evidence A", ToolCallId: "latest-a"));
        context.Add(new("tool", "Latest evidence B", ToolCallId: "latest-b"));
        var plan = await Planner(Policy()).PlanAsync(Connection(4_096), "model", context, [], null, 300,
            TestContext.Current.CancellationToken);
        plan.Fits.ShouldBeTrue();
        plan.OmittedMessages.ShouldBeGreaterThan(0);
        plan.Messages.ShouldContain(message => message.Content == "Keep this exact question");
        plan.Messages.TakeLast(3).ShouldBe(context.TakeLast(3));
        AssertProtocol(plan.Messages);
        context[3].ForModel.Length.ShouldBe(1_000);
    }

    [Fact]
    public void ShouldFailRatherThanDiscardAnOversizedLatestProtocolGroup()
    {
        ChatCompletionMessage[] context = [new("user", "Current question"),
            new("assistant", "", [new("latest", "read", new string('a', 10_000))]),
            new("tool", "Latest evidence", ToolCallId: "latest")];
        var plan = Planner(Policy()).Plan(Connection(4_096), "model", context, []);
        plan.Fits.ShouldBeFalse();
        plan.Messages.ShouldContain(context[0]);
        plan.Messages.ShouldContain(context[1]);
        plan.Messages.ShouldContain(context[2]);
        AssertProtocol(plan.Messages);
    }

    [Theory]
    [InlineData(8_192)]
    [InlineData(16_384)]
    [InlineData(32_768)]
    [InlineData(131_072)]
    public void ShouldKeepLongRunsWithinBudgetWithFewCompactionsAndIntentionalPrefixChanges(long window)
    {
        var connection = Connection(window);
        var policy = Policy();
        var planner = Planner(policy);
        var memory = new ContextCompactionMemory();
        var tracker = new PromptPrefixTracker(_estimator);
        var key = new PromptPrefixKey(Guid.NewGuid(), Guid.NewGuid(), TokenUsagePurpose.Answer);
        var tools = Enumerable.Range(0, 30).Select(index => Tool($"tool_{index:D2}")).ToArray();
        var context = new List<ChatCompletionMessage> { new("system", "Stable instructions"), new("user", "Investigate") };
        IReadOnlyList<AgentTool>? previous = null;
        var compactions = 0;
        var prefixChanges = 0;
        var stableSteps = 0;
        var lastOmitted = 0;
        for (var step = 0; step < 64; step++)
        {
            // Discovery, a checkpoint, and a smaller model each change the request deliberately.
            if (step == 32)
            {
                context = [context[0], context[1], new("user", "Checkpoint: inspected earlier steps.", IsContextSummary: true)];
                lastOmitted = 0;
            }
            if (step == 48) connection = connection with { ContextWindowTokens = 8_192, Model = "smaller-model" };
            var tool = tools[step is 12 or 13 ? 20 : 0];
            var evidence = step == 20 ? new string('x', 20_000) : "Evidence " + new string('x', 300);
            context.Add(new("assistant", "Step completed", [new ChatToolCall($"call-{step}", tool.ModelDefinition.Name, "{}") ]));
            context.Add(new("tool", evidence, ToolCallId: $"call-{step}"));
            var pins = step == 12 ? new HashSet<string>([tool.ModelDefinition.Name]) : null;
            var selection = policy.Choose(connection, "Investigate", context, tools, pins, previous);
            previous = selection.Tools;
            var definitions = selection.Tools.Select(item => item.ModelDefinition).ToArray();
            var plan = planner.Plan(connection, connection.Model, context, definitions, memory: memory);
            plan.Fits.ShouldBeTrue($"step {step}, window {window}");
            plan.Messages.ShouldContain(context[0]);
            plan.Messages.ShouldContain(message => message.Content == "Investigate");
            plan.Messages[^1].ToolCallId.ShouldBe($"call-{step}");
            AssertProtocol(plan.Messages);
            if (plan.OmittedMessages > lastOmitted) compactions++;
            lastOmitted = plan.OmittedMessages;
            var prefix = tracker.Compare(key, tracker.Shape(new ChatCompletionRequest(connection.BaseUrl, connection.Model,
                null, "Investigate", ContextMessages: plan.Messages, Tools: definitions)));
            if (prefix is { Change: not null }) prefixChanges++;
            if (prefix is { Change: null, ReusableTokens: > 0 }) stableSteps++;
            context[^1].ForModel.ShouldBeSameAs(evidence);
        }
        stableSteps.ShouldBeGreaterThan(40);
        compactions.ShouldBeLessThan(16);
        prefixChanges.ShouldBeLessThan(20);
    }

    private AdaptiveContextPolicy Policy() => new(_estimator, new ConnectionContextLimitsResolver());
    private ChatContextPlanner Planner(IAdaptiveContextPolicy policy) => new(_estimator,
        new ChatContextCompactor(_estimator, new ContextSummaryWriter(_estimator, _projector), _projector),
        new ConnectionContextLimitsResolver(), policy);
    private static ConnectionSettings Connection(long window, long output = 1_000) =>
        new(Guid.NewGuid(), "Test", "https://example.test/v1", "model", true, true, false,
            ContextWindowTokens: window, ReservedOutputTokens: output);
    private static AgentTool Tool(string name)
    {
        var schema = JsonDocument.Parse("{}").RootElement.Clone();
        return new(new ChatToolDefinition(name, new string('d', 120), schema),
            ToolDescriptor.Basic(name, name, "Operation", schema), Guid.NewGuid(), name, name);
    }
    private static void AssertProtocol(IReadOnlyList<ChatCompletionMessage> messages)
    {
        var calls = messages.SelectMany(message => message.ToolCalls ?? []).Select(call => call.Id).ToArray();
        var results = messages.Where(message => message.Role == "tool").Select(message => message.ToolCallId).ToArray();
        results.ShouldBe(calls);
    }
    private sealed class Summarizer(string result) : IContextSummarizer
    {
        public Task<string> SummarizeAsync(string prompt, CancellationToken cancellationToken) => Task.FromResult(result);
    }
}
