namespace AI.Infrastructure.Tests.Chat;

using System.Text;
using System.Text.Json;
using AI.Application.Chat;
using AI.Application.Resources;
using AI.Contracts.Chat;
using AI.Infrastructure.Chat;
using Moq;
using Xunit;

/// <summary>Explicit opt-in contract check against a running local OpenAI-compatible model.</summary>
public sealed class LocalModelLiveTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("AI_LOCAL_MODEL_LIVE_TEST") == "1";

    [Fact(Skip = "Set AI_LOCAL_MODEL_LIVE_TEST=1 with a running local model.", SkipUnless = nameof(Enabled))]
    public async Task StreamsToolCallAndContinuesAfterItsResult()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var usage = new ChatCompletionUsageReader();
        var policy = new ChatTransportPolicy();
        var client = new OpenAiCompatibleChatCompletionClient(http, new ChatCompletionSseParser(usage, policy),
            policy, usage, new RateLimitHeaderReader(), new AI.Application.Usage.ConnectionRateLimits(),
            new AI.Infrastructure.Projects.SystemClock(), Mock.Of<IResourceAssetService>());
        var request = new ChatCompletionRequest(
            Environment.GetEnvironmentVariable("AI_LOCAL_MODEL_URL") ?? "http://127.0.0.1:52176/v1",
            Environment.GetEnvironmentVariable("AI_LOCAL_MODEL_NAME") ?? "qwen3.5-2b-local",
            Environment.GetEnvironmentVariable("AI_LOCAL_MODEL_KEY"), "Use get_weather to get the weather in Moscow.",
            Tools: [new ChatToolDefinition("get_weather", "Get weather for a city.",
                JsonSerializer.SerializeToElement(new { type = "object", properties = new { city = new { type = "string" } }, required = (string[])["city"] }))]);
        var calls = new List<ChatToolCall>();
        var preamble = new StringBuilder();
        await foreach (var chunk in client.StreamAsync(request, timeout.Token))
        {
            preamble.Append(chunk.Content);
            if (chunk.ToolCalls is { } tools) calls.AddRange(tools);
        }
        var call = Assert.Single(calls);
        Assert.Equal("get_weather", call.Name);
        using var arguments = JsonDocument.Parse(call.Arguments);
        Assert.Equal("Moscow", arguments.RootElement.GetProperty("city").GetString());
        var continuation = request with { Message = "", ContextMessages = [
            new ChatCompletionMessage("user", request.Message),
            new ChatCompletionMessage("assistant", preamble.ToString(), ToolCalls: calls),
            new ChatCompletionMessage("tool", "{\"temperature_c\":12,\"condition\":\"rain\"}", ToolCallId: call.Id)] };
        var answer = new StringBuilder();
        await foreach (var chunk in client.StreamAsync(continuation, timeout.Token))
        {
            Assert.True(chunk.ToolCalls is not { Count: > 0 }, "The model should use the supplied result instead of calling again.");
            answer.Append(chunk.Content);
        }
        Assert.Contains("12", answer.ToString());
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var receivedText = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var chunk in client.StreamAsync(request with
                { Message = "Count from 1 to 1000, writing every number.", Tools = null }, cancelled.Token))
            {
                if (chunk.Content.Length == 0) continue;
                receivedText = true;
                cancelled.Cancel();
            }
        });
        Assert.True(receivedText, "Cancellation must happen after generation starts.");
    }
}
