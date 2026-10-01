namespace AI.Infrastructure.Chat;

using AI.Application.Chat;

public sealed class ChatTransportPolicy(
    TimeSpan? responseHeadersTimeout = null,
    TimeSpan? firstTokenTimeout = null,
    TimeSpan? retryDeadline = null,
    TimeSpan? streamIdleTimeout = null) : IChatTransportPolicy
{
    public TimeSpan ResponseHeadersTimeout { get; } = responseHeadersTimeout ?? TimeSpan.FromSeconds(30);
    public TimeSpan FirstTokenTimeout { get; } = firstTokenTimeout ?? TimeSpan.FromSeconds(90);
    // Reasoning models think between sentences and tool calls; a gateway that drops the reasoning
    // deltas sends nothing at all while they do, so this allows for a long pause, not a hiccup.
    public TimeSpan StreamIdleTimeout { get; } = streamIdleTimeout ?? TimeSpan.FromSeconds(60);
    public TimeSpan RetryDeadline { get; } = retryDeadline ?? TimeSpan.FromMinutes(10);
}
