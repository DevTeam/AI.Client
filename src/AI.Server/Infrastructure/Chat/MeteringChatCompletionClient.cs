namespace AI.Infrastructure.Chat;

using AI.Application.Chat;
using AI.Application.Usage;
using AI.Contracts.Chat;
using AI.Contracts.Usage;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

/// <summary>
/// Measures every request that reaches a model, whoever makes it. Sitting outside the retries, it
/// sees one request per answer however many attempts the answer took; an attempt that failed
/// before producing anything used nothing worth counting.
/// </summary>
/// <remarks>
/// An endpoint that does not report usage is measured by estimate, and the record says so: a
/// figure the application guessed must never pass for one the provider billed.
/// </remarks>
public sealed class MeteringChatCompletionClient(
    IChatCompletionClient inner,
    ITokenUsageMeter meter,
    IContextTokenEstimator estimator,
    IPromptPrefixTracker prefixes, IAdaptiveContextPolicy? contextPolicy = null) : IChatCompletionClient
{
    public async Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var response = await inner.CompleteAsync(request, cancellationToken);
        await meter.RecordAsync(Measure(request, response.Model, response.Usage, response.Content, [], watch.Elapsed, null),
            cancellationToken);
        return response;
    }

    public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        TimeSpan? firstToken = null;
        ChatCompletionUsage? usage = null;
        string? model = null;
        var content = new StringBuilder();
        var calls = new List<ChatToolCall>();
        try
        {
            await foreach (var chunk in inner.StreamAsync(request, cancellationToken))
            {
                firstToken ??= watch.Elapsed;
                model = chunk.Model ?? model;
                usage = chunk.Usage ?? usage;
                content.Append(chunk.Content);
                if (chunk.ToolCalls is { } received) calls.AddRange(received);
                yield return chunk;
            }
        }
        finally
        {
            // A stream abandoned part-way — stopped by the person, or failed after it began — was
            // paid for up to where it stopped, so it is recorded too.
            if (firstToken is not null)
                await meter.RecordAsync(Measure(request, model, usage, content.ToString(), calls, watch.Elapsed, firstToken),
                    CancellationToken.None);
        }
    }

    private TokenUsageMeasurement Measure(ChatCompletionRequest request, string? model, ChatCompletionUsage? usage,
        string content, List<ChatToolCall> calls, TimeSpan duration, TimeSpan? firstToken)
    {
        var name = string.IsNullOrWhiteSpace(model) ? request.Model.Trim() : model;
        var shape = prefixes.Shape(request);
        if (usage is not null)
        {
            contextPolicy?.ObserveInputUsage(request, usage.Tokens.InputTokens);
            return new TokenUsageMeasurement(name, request.CredentialProfileId, usage.Tokens, false, duration, firstToken,
                usage.Cost, shape);
        }
        IReadOnlyList<ChatCompletionMessage> sent = request.ContextMessages is { Count: > 0 } messages
            ? messages
            : [new ChatCompletionMessage("user", request.Message)];
        var modelEstimator = estimator.ForModel(request.Model);
        var input = modelEstimator.EstimateMessages(sent) + (request.Tools is { Count: > 0 } tools ? modelEstimator.EstimateTools(tools) : 0);
        var output = modelEstimator.EstimateMessages([new ChatCompletionMessage("assistant", content, calls.Count > 0 ? calls : null)]);
        return new TokenUsageMeasurement(name, request.CredentialProfileId, new TokenCounts(input, output), true, duration,
            firstToken, Shape: shape);
    }
}
