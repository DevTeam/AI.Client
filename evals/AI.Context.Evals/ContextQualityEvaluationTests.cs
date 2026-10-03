namespace AI.Application.Tests.Chat;

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AI.Application.Chat;
using AI.Contracts.Settings;
using AI.Infrastructure.Chat;
using Shouldly;
using Xunit;

/// <summary>Paid model evaluations are opt-in and separate from deterministic correctness tests.</summary>
public sealed class ContextQualityEvaluationTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };
    [Fact]
    [Trait("Category", "ModelEvaluation")]
    public async Task ShouldPreserveContinuationQualityAndReportStrategyCosts()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("AI_CONTEXT_EVAL") == "1",
            "Set AI_CONTEXT_EVAL=1 and a dedicated endpoint/model to run model evaluations.");
        var url = Environment.GetEnvironmentVariable("AI_CONTEXT_EVAL_BASE_URL");
        var model = Environment.GetEnvironmentVariable("AI_CONTEXT_EVAL_MODEL");
        Assert.True(Uri.TryCreate(url, UriKind.Absolute, out var endpoint) && endpoint.Scheme is "http" or "https",
            "AI_CONTEXT_EVAL_BASE_URL must be an absolute HTTP or HTTPS URL.");
        Assert.False(string.IsNullOrWhiteSpace(model), "AI_CONTEXT_EVAL_MODEL is required.");
        var window = long.Parse(Environment.GetEnvironmentVariable("AI_CONTEXT_EVAL_WINDOW") ?? "8192",
            System.Globalization.CultureInfo.InvariantCulture);
        var reserve = long.Parse(Environment.GetEnvironmentVariable("AI_CONTEXT_EVAL_OUTPUT") ?? "1000",
            System.Globalization.CultureInfo.InvariantCulture);
        var connection = new ConnectionSettings(Guid.NewGuid(), "Evaluation", url!, model!, true, true, false,
            ContextWindowTokens: window, ReservedOutputTokens: reserve);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var key = Environment.GetEnvironmentVariable("AI_CONTEXT_EVAL_API_KEY");
        if (!string.IsNullOrWhiteSpace(key)) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var estimator = new ContextTokenEstimator(new ContextTextTokenizer());
        var policy = new AdaptiveContextPolicy(estimator, new ConnectionContextLimitsResolver());
        var writer = new ContextSummaryWriter(estimator, new ToolResultContextProjector(), policy);
        var allReports = new List<object>();
        var errors = new List<string>();
        var calls = 0;
        long reportedInput = 0, reportedOutput = 0, cachedInput = 0;
        var usageResponses = 0;
        var summarizer = new Summarizer(async prompt => await Send([new("user", prompt)]));

        foreach (var scenario in ContextRetentionScenario.All)
        {
            var source = scenario.Messages.Concat(Enumerable.Range(0, 4).Select(_ =>
                new ChatCompletionMessage("assistant", string.Concat(Enumerable.Repeat("An unrelated progress log. ", 30))))).ToArray();
            foreach (var strategy in new[] { "full", "deterministic", "repeated_llm" })
            {
                calls = 0; reportedInput = 0; reportedOutput = 0; cachedInput = 0; usageResponses = 0;
                var started = Stopwatch.GetTimestamp();
                IReadOnlyList<ChatCompletionMessage> context = source;
                if (strategy == "deterministic")
                    context = new ChatContextCompactor(estimator, writer, new ToolResultContextProjector())
                        .Compact(source, 512, connection.Model).Messages;
                if (strategy == "repeated_llm")
                {
                    for (var generation = 0; generation < 4; generation++)
                    {
                        var summary = await writer.WriteAsync(context, 512, summarizer,
                            TestContext.Current.CancellationToken, connection);
                        Assert.NotNull(summary);
                        context = [new("user", summary.Text, IsContextSummary: true),
                            new("assistant", "Another unrelated step completed; earlier constraints and remaining work still apply.")];
                    }
                }
                var question = new ChatCompletionMessage("user",
                    "List the earlier constraints, decisions with their reasons, evidence, failures, rejected alternatives, "
                    + "and pending work. Preserve exact identifiers and paths. Do not invent success.");
                var answer = await Send([.. context, question]);
                var missing = scenario.RequiredFacts.Where(fact => !answer.Contains(fact, StringComparison.OrdinalIgnoreCase)).ToArray();
                allReports.Add(new { scenario = scenario.Name, strategy, matched = scenario.RequiredFacts.Count - missing.Length,
                    expected = scenario.RequiredFacts.Count, calls, usageResponses,
                    reportedInput = usageResponses > 0 ? (long?)reportedInput : null,
                    reportedOutput = usageResponses > 0 ? (long?)reportedOutput : null,
                    cachedInput = usageResponses > 0 ? (long?)cachedInput : null,
                    elapsedMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds });
                // The deterministic baseline may lose information; measure it rather than assert parity.
                if (strategy != "deterministic" && missing.Length > 0)
                    errors.Add($"{scenario.Name}/{strategy}: missing {string.Join(", ", missing)}");
            }
        }
        output.WriteLine(JsonSerializer.Serialize(allReports, ReportOptions));
        errors.ShouldBeEmpty();

        async Task<string> Send(IReadOnlyList<ChatCompletionMessage> messages)
        {
            estimator.ForModel(connection.Model).EstimateMessages(messages).ShouldBeLessThanOrEqualTo(policy.Resolve(connection).UsableTokens);
            using var response = await http.PostAsJsonAsync(url!.TrimEnd('/') + "/chat/completions",
                new { model, messages = messages.Select(message => new { role = message.Role, content = message.ForModel }), stream = false },
                TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
                cancellationToken: TestContext.Current.CancellationToken);
            calls++;
            if (json.RootElement.TryGetProperty("usage", out var usage))
            {
                usageResponses++;
                if (usage.TryGetProperty("prompt_tokens", out var input)) reportedInput += input.GetInt64();
                if (usage.TryGetProperty("completion_tokens", out var generated)) reportedOutput += generated.GetInt64();
                if (usage.TryGetProperty("prompt_tokens_details", out var details) && details.TryGetProperty("cached_tokens", out var cached))
                    cachedInput += cached.GetInt64();
            }
            return json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }
    }

    private sealed class Summarizer(Func<string, Task<string>> summarize) : IContextSummarizer
    {
        public Task<string> SummarizeAsync(string prompt, CancellationToken cancellationToken) => summarize(prompt);
    }
}
