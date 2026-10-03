namespace AI.Infrastructure.Tests.Chat;

using System.Text.Json;
using AI.Application.Chat;
using AI.Contracts.Settings;
using AI.Infrastructure.Chat;
using Shouldly;
using Xunit;

public sealed class ContextTextTokenizerTests
{
    [Fact]
    public async Task ShouldGateUnicodePartsAndMergesWithTheSameKnownModelTokenizer()
    {
        var estimator = new ContextTokenEstimator(new ContextTextTokenizer());
        var policy = new AdaptiveContextPolicy(estimator, new ConnectionContextLimitsResolver());
        var connection = new ConnectionSettings(Guid.NewGuid(), "Test", "https://example.test/v1", "gpt-4o", true, true, false,
            ContextWindowTokens: 4_096, ReservedOutputTokens: 512);
        var prompts = new List<string>();
        var source = string.Concat(Enumerable.Repeat("Error CS1001: C:/repo/Parser.cs 🚀 中文\n", 400));
        var writer = new ContextSummaryWriter(estimator, new ToolResultContextProjector(), policy);
        var summary = await writer.WriteAsync([new("user", source)], 512, new Summarizer(prompt =>
        {
            prompts.Add(prompt);
            estimator.ForModel(connection.Model).EstimateMessages([new("user", prompt)])
                .ShouldBeLessThanOrEqualTo(policy.Resolve(connection).UsableTokens);
            return "Failures: CS1001 at C:/repo/Parser.cs. Remaining work: verify the repair 🚀 中文.";
        }), TestContext.Current.CancellationToken, connection);
        summary.ShouldNotBeNull();
        prompts.Count.ShouldBeGreaterThan(1);
        prompts[^1].ShouldStartWith("Merge");
        var reconstructed = string.Concat(prompts.Where(prompt => !prompt.StartsWith("Merge", StringComparison.Ordinal))
            .Select(prompt => prompt[(prompt.IndexOf("\n\n", StringComparison.Ordinal) + 2)..]));
        reconstructed.ShouldBe($"[user] {source}\n");
        estimator.ForModel(connection.Model).EstimateMessages([new("user", summary.Text)]).ShouldBeLessThanOrEqualTo(512);
    }
    [Theory]
    [InlineData("gpt-4o")]
    [InlineData("gpt-4")]
    public void ShouldCountKnownModelsOffline(string model)
    {
        var tokenizer = new ContextTextTokenizer();
        tokenizer.TryCount(model, "Hello, world!", out var tokens).ShouldBeTrue();
        tokens.ShouldBe(4);
        var bound = new ContextTokenEstimator(tokenizer).ForModel(model);
        bound.EstimateMessages([new("user", "Hello, world!")]).ShouldBe(17);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("local-model")]
    [InlineData("provider/custom-gpt-4o")]
    public void ShouldKeepConservativeFallbackForUnknownModels(string? model)
    {
        var estimator = new ContextTokenEstimator(new ContextTextTokenizer()).ForModel(model);
        var fallback = new ContextTokenEstimator();
        ChatCompletionMessage[] messages = [new("user", "Ошибка 🚀 中文", [new("call", "read", "{\"path\":\"file.cs\"}")])];
        estimator.EstimateMessages(messages).ShouldBe(fallback.EstimateMessages(messages));
        ChatToolDefinition[] tools = [new("read", "Read a file", JsonDocument.Parse("{}").RootElement.Clone())];
        estimator.EstimateTools(tools).ShouldBe(fallback.EstimateTools(tools));
    }

    [Fact]
    public async Task ShouldKeepModelViewsIndependentAcrossConcurrentConsumers()
    {
        var root = new ContextTokenEstimator(new ContextTextTokenizer());
        var text = "Text tokenization is the process of splitting a string into a list of tokens.";
        var exact = root.ForModel("gpt-4o");
        var unknown = root.ForModel("local-model");
        var expected = new ContextTokenEstimator().EstimateMessages([new("user", text)]);
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            exact.EstimateMessages([new("user", text)]).ShouldBe(29);
            unknown.EstimateMessages([new("user", text)]).ShouldBe(expected);
            root.EstimateMessages([new("user", text)]).ShouldBe(expected);
        }, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public void ShouldUseTheSameModelEstimateAtThePlannerAndUsageCalibration()
    {
        var estimator = new ContextTokenEstimator(new ContextTextTokenizer());
        var samples = new ContextEstimateSamples();
        var policy = new AdaptiveContextPolicy(estimator, new ConnectionContextLimitsResolver(), samples);
        var connection = new ConnectionSettings(Guid.NewGuid(), "Test", "https://example.test/v1", "gpt-4o", true, true, false,
            ContextWindowTokens: 8_192, ReservedOutputTokens: 1_000);
        ChatCompletionMessage[] messages = [new("user", "Hello, world!")];
        var planner = new ChatContextPlanner(estimator, new ChatContextCompactor(estimator,
            new ContextSummaryWriter(estimator, new ToolResultContextProjector(), policy), new ToolResultContextProjector()),
            new ConnectionContextLimitsResolver(), policy);
        var plan = planner.Plan(connection, connection.Model, messages, []);
        plan.EstimatedInputTokens.ShouldBe(17);
        policy.ObserveInputUsage(new(connection.BaseUrl, connection.Model, null, "", connection.Id, messages), 17);
        samples.Read(connection.Id, connection.BaseUrl, connection.Model).Single().EstimatedTokens.ShouldBe(17);
    }

    private sealed class Summarizer(Func<string, string> summarize) : IContextSummarizer
    {
        public Task<string> SummarizeAsync(string prompt, CancellationToken cancellationToken) => Task.FromResult(summarize(prompt));
    }
}
