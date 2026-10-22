namespace AI.Infrastructure.Chat;

using AI.Application.Chat;

public sealed class ChatTransportPolicy(
    TimeSpan? responseHeadersTimeout = null,
    TimeSpan? firstTokenTimeout = null,
    TimeSpan? retryDeadline = null,
    TimeSpan? streamIdleTimeout = null) : IChatTransportPolicy
{
    public TimeSpan ResponseHeadersTimeout { get; } = responseHeadersTimeout ?? TimeSpan.FromSeconds(30);
    /// <summary>Prompt reading time allowed per thousand input tokens, on top of <see cref="FirstTokenTimeout"/>.</summary>
    private static readonly TimeSpan PrefillPerThousandTokens = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan MaxFirstTokenTimeout = TimeSpan.FromMinutes(10);

    public TimeSpan FirstTokenTimeout { get; } = firstTokenTimeout ?? TimeSpan.FromSeconds(90);

    // A slow gateway reads a long prompt for minutes before it sends anything. A fixed limit cuts it
    // off just before the answer, and asking again makes it read the whole prompt from the start.
    public TimeSpan FirstTokenTimeoutFor(long inputCharacters)
    {
        // About four characters a token, the same estimate the context planner falls back on.
        var seconds = FirstTokenTimeout.TotalSeconds
                      + PrefillPerThousandTokens.TotalSeconds * (Math.Max(0, inputCharacters) / 4000.0);
        return seconds < MaxFirstTokenTimeout.TotalSeconds ? TimeSpan.FromSeconds(seconds) : MaxFirstTokenTimeout;
    }
    // Reasoning models think between sentences and tool calls; a gateway that drops the reasoning
    // deltas sends nothing at all while they do, so this allows for a long pause, not a hiccup.
    public TimeSpan StreamIdleTimeout { get; } = streamIdleTimeout ?? TimeSpan.FromSeconds(60);
    public TimeSpan RetryDeadline { get; } = retryDeadline ?? TimeSpan.FromMinutes(10);
}
