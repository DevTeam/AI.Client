namespace AI.Application.Chat;

public interface IChatTransportPolicy
{
    TimeSpan ResponseHeadersTimeout { get; }
    TimeSpan FirstTokenTimeout { get; }

    /// <summary>
    /// How long a request of this size may go before its first token: <see cref="FirstTokenTimeout"/>
    /// plus the time an endpoint needs to read the prompt, which grows with it.
    /// </summary>
    TimeSpan FirstTokenTimeoutFor(long inputCharacters);

    /// <summary>
    /// How long a stream that has started may go without a single line before it counts as stalled.
    /// </summary>
    TimeSpan StreamIdleTimeout { get; }
    TimeSpan RetryDeadline { get; }
}
