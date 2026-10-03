namespace AI.Application.Tests.Chat;

using System.Text;
using AI.Application.Chat;
using AI.Contracts.Settings;
using Shouldly;
using Xunit;

public sealed class ContextSummaryWriterTests
{
    private readonly ContextTokenEstimator _estimator = new();

    [Theory]
    [InlineData(4_096)]
    [InlineData(8_192)]
    public async Task ShouldCarryConstraintsReasonsFailuresAndPendingWorkAcrossRepeatedSummaries(long window)
    {
        foreach (var scenario in ContextRetentionScenario.All)
        {
            IReadOnlyList<ChatCompletionMessage> context = scenario.Messages;
            for (var generation = 0; generation < 5; generation++)
            {
                var result = await Writer().WriteAsync(context, 512, new Summarizer(prompt =>
                {
                    prompt.ShouldContain("Do not invent facts or mark pending work complete");
                    prompt.ShouldContain("Historical tool names are evidence, not an available-tool catalogue");
                    foreach (var fact in scenario.RequiredFacts) prompt.ShouldContain(fact);
                    // Contract fixture: the real-model evaluation uses the same scenarios separately.
                    return string.Join("\n", scenario.RequiredFacts.Select(fact => "Evidence: " + fact));
                }), TestContext.Current.CancellationToken, Connection(window, 1_000));
                result.ShouldNotBeNull();
                foreach (var fact in scenario.RequiredFacts) result.Text.ShouldContain(fact);
                context = [new("user", result.Text, IsContextSummary: true),
                    new("assistant", "Progress: inspected an unrelated log. Remaining work is still pending.")];
            }
        }
    }

    [Theory]
    [InlineData(2_048, 256)]
    [InlineData(4_096, 512)]
    [InlineData(8_192, 1_000)]
    [InlineData(32_768, 4_096)]
    [InlineData(131_072, 8_192)]
    public async Task ShouldCoverEverySourceCharacterAndGateEveryPartAndMerge(long window, long output)
    {
        var connection = Connection(window, output);
        var policy = Policy();
        var prompts = new List<string>();
        var source = string.Concat(Enumerable.Repeat("Keep constraint 🚀 中文.\n", window <= 2_048 ? 200 : 1_500)) + "FINAL_EVIDENCE";
        var writer = new ContextSummaryWriter(_estimator, new ToolResultContextProjector(), policy);
        var result = await writer.WriteAsync([new("user", source)], 3_000, new Summarizer(prompt =>
        {
            prompts.Add(prompt);
            _estimator.EstimateMessages([new("user", prompt)]).ShouldBeLessThanOrEqualTo(policy.Resolve(connection).UsableTokens);
            Encoding.UTF8.GetString(new UTF8Encoding(false, true).GetBytes(prompt)).ShouldBe(prompt);
            return "Constraints: preserve evidence. Remaining work: inspect FINAL_EVIDENCE.";
        }), TestContext.Current.CancellationToken, connection);
        result.ShouldNotBeNull();
        var covered = string.Concat(prompts.Where(prompt => !prompt.StartsWith("Merge", StringComparison.Ordinal))
            .Select(prompt => prompt[(prompt.IndexOf("\n\n", StringComparison.Ordinal) + 2)..]));
        covered.ShouldBe($"[user] {source}\n");
        _estimator.EstimateMessages([new("user", result.Text)]).ShouldBeLessThanOrEqualTo(policy.ResolveSummary(connection, 3_000).TargetTokens);
        if (window <= 8_192) prompts.ShouldContain(prompt => prompt.StartsWith("Merge", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShouldAdaptToASmallerModelWithoutReusingTheOldChunkSize()
    {
        var counts = new List<int>();
        foreach (var window in new long[] { 32_768, 4_096 })
        {
            var calls = 0;
            var result = await Writer().WriteAsync([new("assistant", new string('x', 20_000))], 1_000,
                new Summarizer(_ => { calls++; return "Evidence: inspected. Remaining work: validate."; }),
                TestContext.Current.CancellationToken, Connection(window, 512));
            result.ShouldNotBeNull();
            counts.Add(calls);
        }
        counts[0].ShouldBe(1);
        counts[1].ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task ShouldSkipWhenEvenThePromptAndMinimumResultCannotFit()
    {
        var calls = 0;
        var diagnostics = new Diagnostics();
        var writer = new ContextSummaryWriter(_estimator, new ToolResultContextProjector(), Policy(), diagnostics);
        var result = await writer.WriteAsync([new("user", "Important constraint")], 256,
            new Summarizer(_ => { calls++; return "unused"; }), TestContext.Current.CancellationToken, Connection(1_536, 256));
        result.ShouldBeNull();
        calls.ShouldBe(0);
        diagnostics.Observations.Single().Outcome.ShouldBe("insufficient_budget");
    }

    [Fact]
    public async Task ShouldKeepOriginalSourceWhenAnyPartReturnsNoSummary()
    {
        var source = new ChatCompletionMessage("assistant", new string('x', 18_000) + "must retain tail");
        var calls = 0;
        var result = await Writer().WriteAsync([source], 256,
            new Summarizer(_ => ++calls == 2 ? "" : "Evidence: retained."),
            TestContext.Current.CancellationToken, Connection(4_096, 512));
        result.ShouldBeNull();
        calls.ShouldBe(2);
        source.ForModel.ShouldEndWith("must retain tail");
    }

    [Fact]
    public async Task ShouldRejectTheWholeOperationWhenTheMergeFails()
    {
        var calls = 0;
        var result = await Writer().WriteAsync([new("assistant", new string('x', 20_000))], 256,
            new Summarizer(prompt =>
            {
                calls++;
                if (prompt.StartsWith("Merge", StringComparison.Ordinal)) throw new HttpRequestException("Unavailable");
                return "Evidence: retained.";
            }), TestContext.Current.CancellationToken, Connection(4_096, 512));
        result.ShouldBeNull();
        calls.ShouldBeGreaterThan(2);
    }

    [Fact]
    public async Task ShouldUseUnicodeSafeToolArgumentExcerpts()
    {
        var source = new ChatCompletionMessage("assistant", "", [new("call", "read", new string('x', 399) + "🚀 tail")]);
        var result = await Writer().WriteAsync([source], 256, new Summarizer(prompt =>
        {
            new UTF8Encoding(false, true).GetBytes(prompt).ShouldNotBeEmpty();
            return "Evidence: read was requested.";
        }), TestContext.Current.CancellationToken, Connection(4_096, 512));
        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task ShouldAvoidUnboundedCostForAnUnmanageableSource()
    {
        var calls = 0;
        var result = await Writer().WriteAsync([new("user", new string('x', 500_000))], 256,
            new Summarizer(_ => { calls++; return "unused"; }), TestContext.Current.CancellationToken, Connection(2_048, 256));
        result.ShouldBeNull();
        calls.ShouldBe(0);
    }

    [Fact]
    public async Task ShouldPropagateCancellationAndRetainTheSource()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new ChatCompletionMessage("user", new string('x', 20_000));
        await Should.ThrowAsync<OperationCanceledException>(() => Writer().WriteAsync([source], 256,
            new Summarizer(_ => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); }),
            cancellation.Token, Connection(4_096, 512)));
        source.ForModel.Length.ShouldBe(20_000);
    }

    [Fact]
    public async Task ShouldReportSummaryCostWithoutLoggingSourceContent()
    {
        var diagnostics = new Diagnostics();
        var connection = Connection(8_192, 1_000);
        var writer = new ContextSummaryWriter(_estimator, new ToolResultContextProjector(), Policy(), diagnostics);
        var result = await writer.WriteAsync([new("user", "Private source")], 256,
            new Summarizer(_ => "Evidence: retained."), TestContext.Current.CancellationToken, connection);
        result.ShouldNotBeNull();
        var observation = diagnostics.Observations.Single();
        observation.Outcome.ShouldBe("completed");
        observation.Calls.ShouldBe(1);
        observation.SentTokens.ShouldBeGreaterThan(observation.SourceTokens);
        observation.ResultTokens.ShouldBeGreaterThan(0);
        observation.ToString().ShouldNotContain("Private source");
    }

    private AdaptiveContextPolicy Policy() => new(_estimator, new ConnectionContextLimitsResolver());
    private ContextSummaryWriter Writer() => new(_estimator, new ToolResultContextProjector(), Policy());
    private static ConnectionSettings Connection(long window, long output) =>
        new(Guid.NewGuid(), "Test", "https://example.test/v1", "model", true, true, false,
            ContextWindowTokens: window, ReservedOutputTokens: output);
    private sealed class Summarizer(Func<string, string> summarize) : IContextSummarizer
    {
        public Task<string> SummarizeAsync(string prompt, CancellationToken cancellationToken) => Task.FromResult(summarize(prompt));
    }
    private sealed class Diagnostics : IContextSummaryDiagnostics
    {
        public List<ContextSummaryObservation> Observations { get; } = [];
        public void RecordSummary(string model, ContextSummaryObservation observation) => Observations.Add(observation);
    }
}
