namespace AI.Infrastructure.Chat;

using AI.Application.Chat;
using AI.Contracts.Usage;
using System.Text.Json;

/// <summary>
/// Understands the spellings OpenAI-compatible endpoints actually use. They agree on
/// <c>prompt_tokens</c> and <c>completion_tokens</c> and disagree on the rest: OpenAI nests cached
/// and reasoning counts in <c>*_details</c>, DeepSeek reports cache hits at the top level, and
/// Anthropic-style gateways use <c>input_tokens</c>/<c>output_tokens</c> with a separate cache read.
/// </summary>
public sealed class ChatCompletionUsageReader : IChatCompletionUsageReader
{
    public ChatCompletionUsage? Read(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object
            || !response.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return null;
        var input = Count(usage, "prompt_tokens") ?? Count(usage, "input_tokens") ?? 0;
        var output = Count(usage, "completion_tokens") ?? Count(usage, "output_tokens") ?? 0;
        // A usage object of zeros is what some servers send when they count nothing; it is no
        // report, and taking it for one would record a request as free.
        if (input <= 0 && output <= 0) return null;
        var cached = Nested(usage, "prompt_tokens_details", "cached_tokens")
                     ?? Nested(usage, "input_tokens_details", "cached_tokens")
                     ?? Count(usage, "prompt_cache_hit_tokens")
                     ?? Count(usage, "cache_read_input_tokens")
                     ?? 0;
        var reasoning = Nested(usage, "completion_tokens_details", "reasoning_tokens")
                        ?? Nested(usage, "output_tokens_details", "reasoning_tokens")
                        ?? 0;
        var cost = usage.TryGetProperty("cost", out var costElement) && costElement.ValueKind == JsonValueKind.Number
                   && costElement.TryGetDecimal(out var value) && value >= 0
            ? value
            : (decimal?)null;
        return new ChatCompletionUsage(
            new TokenCounts(input, output, Math.Clamp(cached, 0, input), Math.Clamp(reasoning, 0, output)), cost);
    }

    private static long? Nested(JsonElement usage, string details, string name) =>
        usage.TryGetProperty(details, out var element) && element.ValueKind == JsonValueKind.Object
            ? Count(element, name)
            : null;

    private static long? Count(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var count) && count >= 0
            ? count
            : null;
}
