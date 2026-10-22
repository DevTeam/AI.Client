namespace AI.Application.Usage;

using System.Collections.Concurrent;
using AI.Contracts.Usage;
using Chat;

/// <summary>
/// What a request started with, kept as fingerprints rather than text: enough to tell where the
/// next request of the branch first differs, and how much came before that.
/// </summary>
public sealed record PromptShape(int ToolsHash, long ToolTokens, IReadOnlyList<int> MessageHashes,
    IReadOnlyList<bool> SystemMessages, IReadOnlyList<long> MessageTokens, string? Model = null, string? BaseUrl = null);

/// <summary>Requests compared with each other: those of one branch, made for one purpose.</summary>
public readonly record struct PromptPrefixKey(Guid ChatId, Guid? BranchId, TokenUsagePurpose Purpose,
    Guid? ConnectionId = null, string? Model = null, string? BaseUrl = null);

/// <summary>
/// Compares application request fingerprints. Provider serialization, cache support and cache
/// lifetime are unknown, so an overlap estimate is not a promise of provider cache hits.
/// </summary>
public interface IPromptPrefixTracker
{
    PromptShape Shape(ChatCompletionRequest request);

    /// <summary>How <paramref name="shape"/> starts compared with the previous request under <paramref name="key"/>; null for the first.</summary>
    PromptPrefix? Compare(PromptPrefixKey key, PromptShape shape);
}

public sealed class PromptPrefixTracker(IContextTokenEstimator estimator) : IPromptPrefixTracker
{
    /// <summary>Branches remembered at once; the oldest go first, and a forgotten one starts over.</summary>
    private const int MaximumKeys = 512;

    private readonly ConcurrentDictionary<PromptPrefixKey, (PromptShape Shape, long Order)> _previous = new();
    private long _order;

    public PromptShape Shape(ChatCompletionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var modelEstimator = estimator.ForModel(request.Model);
        var tools = request.Tools ?? [];
        var toolsHash = new HashCode();
        foreach (var tool in tools)
        {
            toolsHash.Add(tool.Name, StringComparer.Ordinal);
            toolsHash.Add(tool.Description, StringComparer.Ordinal);
            toolsHash.Add(tool.InputSchema.GetRawText(), StringComparer.Ordinal);
        }
        IReadOnlyList<ChatCompletionMessage> messages = request.ContextMessages is { Count: > 0 } context
            ? context
            : [new ChatCompletionMessage("user", request.Message)];
        var hashes = new int[messages.Count];
        var system = new bool[messages.Count];
        var tokens = new long[messages.Count];
        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            var hash = new HashCode();
            hash.Add(message.Role, StringComparer.Ordinal);
            hash.Add(message.ForModel, StringComparer.Ordinal);
            hash.Add(message.ToolCallId, StringComparer.Ordinal);
            foreach (var call in message.ToolCalls ?? [])
            {
                hash.Add(call.Id, StringComparer.Ordinal);
                hash.Add(call.Name, StringComparer.Ordinal);
                hash.Add(call.Arguments, StringComparer.Ordinal);
            }
            hashes[index] = hash.ToHashCode();
            system[index] = message.Role == "system";
            tokens[index] = modelEstimator.EstimateMessages([message]);
        }
        return new PromptShape(toolsHash.ToHashCode(), tools.Count == 0 ? 0 : modelEstimator.EstimateTools(tools), hashes, system, tokens,
            request.Model, request.BaseUrl);
    }

    public PromptPrefix? Compare(PromptPrefixKey key, PromptShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var order = Interlocked.Increment(ref _order);
        PromptShape? previous = _previous.TryGetValue(key, out var entry) ? entry.Shape : null;
        _previous[key] = (shape, order);
        if (_previous.Count > MaximumKeys)
            foreach (var stale in _previous.OrderBy(item => item.Value.Order).Take(_previous.Count - MaximumKeys).ToArray())
                _previous.TryRemove(stale.Key, out _);
        if (previous is null) return null;
        var input = shape.ToolTokens + shape.MessageTokens.Sum();
        if (previous.ToolsHash != shape.ToolsHash) return new PromptPrefix(0, PromptPrefixChange.Tools, input);

        var shared = 0;
        while (shared < previous.MessageHashes.Count && shared < shape.MessageHashes.Count
               && previous.MessageHashes[shared] == shape.MessageHashes[shared])
            shared++;
        var reusable = shape.ToolTokens + shape.MessageTokens.Take(shared).Sum();
        // The previous request's last message is often not in the next one — a step's guidance, or
        // an answer carried on — and losing it costs only itself, not what came before it.
        if (shared >= previous.MessageHashes.Count - 1) return new PromptPrefix(reusable, null, input);
        return new PromptPrefix(reusable, previous.SystemMessages[shared]
            ? PromptPrefixChange.Instructions
            : PromptPrefixChange.History, input);
    }
}
